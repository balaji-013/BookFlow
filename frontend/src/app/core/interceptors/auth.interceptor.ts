import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { BehaviorSubject, catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { environment } from '../../../environments/environment';

let refreshing = false;
const refreshed$ = new BehaviorSubject<string | null>(null);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  if (!req.url.startsWith(environment.apiUrl)) return next(req);

  const isAuthCall = /\/auth\/(login|refresh|register-tenant)$/.test(req.url);
  const withToken = (r: HttpRequest<unknown>, token: string | null) => token ? r.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : r;

  return next(withToken(req, auth.accessToken)).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || isAuthCall || !auth.refreshToken) {
        if (err.status === 401 && !isAuthCall) auth.logout();
        return throwError(() => err);
      }
      if (!refreshing) {
        refreshing = true;
        refreshed$.next(null);
        return auth.refresh().pipe(
          switchMap(r => { refreshing = false; refreshed$.next(r.accessToken); return next(withToken(req, r.accessToken)); }),
          catchError(e => { refreshing = false; auth.logout(); return throwError(() => e); })
        );
      }
      return refreshed$.pipe(filter((t): t is string => !!t), take(1), switchMap(t => next(withToken(req, t))));
    })
  );
};
