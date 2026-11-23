import { Component, Inject, inject } from '@angular/core';
import { MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MATERIAL, InrPipe } from '../../shared/shared';
import { Statement } from '../../core/models';

@Component({
  standalone: true,
  imports: [...MATERIAL, InrPipe],
  template: `
    <h2 mat-dialog-title>{{ data.statement.partyName }} — statement</h2>
    <mat-dialog-content>
      <p class="muted">Opening balance {{ data.statement.openingBalance | inr }} · Closing balance <strong>{{ data.statement.closingBalance | inr }}</strong>
        ({{ data.kind === 'customer' ? 'amount the customer owes you' : 'amount you owe the supplier' }})</p>
      <table mat-table [dataSource]="data.statement.lines">
        <ng-container matColumnDef="date"><th mat-header-cell *matHeaderCellDef>Date</th><td mat-cell *matCellDef="let l">{{ l.date | date:'d MMM yyyy' }}</td></ng-container>
        <ng-container matColumnDef="type"><th mat-header-cell *matHeaderCellDef>Type</th><td mat-cell *matCellDef="let l">{{ l.type }}</td></ng-container>
        <ng-container matColumnDef="number"><th mat-header-cell *matHeaderCellDef>Ref</th><td mat-cell *matCellDef="let l">{{ l.number }}</td></ng-container>
        <ng-container matColumnDef="debit"><th mat-header-cell *matHeaderCellDef class="num">Debit</th><td mat-cell *matCellDef="let l" class="num">{{ l.debit ? (l.debit | inr) : '' }}</td></ng-container>
        <ng-container matColumnDef="credit"><th mat-header-cell *matHeaderCellDef class="num">Credit</th><td mat-cell *matCellDef="let l" class="num">{{ l.credit ? (l.credit | inr) : '' }}</td></ng-container>
        <ng-container matColumnDef="balance"><th mat-header-cell *matHeaderCellDef class="num">Balance</th><td mat-cell *matCellDef="let l" class="num">{{ l.balance | inr }}</td></ng-container>
        <tr mat-header-row *matHeaderRowDef="cols"></tr>
        <tr mat-row *matRowDef="let row; columns: cols"></tr>
      </table>
      @if (!data.statement.lines.length) { <div class="empty">No transactions.</div> }
    </mat-dialog-content>
    <mat-dialog-actions align="end"><button mat-button mat-dialog-close>Close</button></mat-dialog-actions>`,
})
export class StatementDialogComponent {
  readonly data = inject<{ statement: Statement; kind: 'customer' | 'supplier' }>(MAT_DIALOG_DATA);
  cols = ['date', 'type', 'number', 'debit', 'credit', 'balance'];
  }
