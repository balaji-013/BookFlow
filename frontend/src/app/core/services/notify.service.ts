import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ProblemDetails } from '../models';

@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snack = inject(MatSnackBar);

  success(message: string) { this.snack.open(message, 'OK', { duration: 3000 }); }
  info(message: string) { this.snack.open(message, undefined, { duration: 4000 }); }

  error(err: unknown, fallback = 'Something went wrong') {
    this.snack.open(this.describe(err, fallback), 'Dismiss', { duration: 7000, panelClass: 'snack-error' });
  }

  describe(err: unknown, fallback = 'Something went wrong'): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 0) return 'Cannot reach the server. Check your connection.';
      const p = err.error as ProblemDetails | undefined;
      if (p?.errors) return Object.values(p.errors).flat().join(' ');
      if (p?.detail) return p.detail;
      if (p?.title) return p.title;
      if (err.status === 429) return 'Too many requests, please wait a minute.';
      if (err.status === 403) return 'You do not have permission for this action.';
    }
    if (err instanceof Error) return err.message;
    return fallback;
  }
}
