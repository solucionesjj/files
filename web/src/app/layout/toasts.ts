import { Component } from '@angular/core';
import { ToastService } from '../core/http/toast.service';

/** Renders the toast queue with role=status for screen readers. */
@Component({
  selector: 'app-toasts',
  standalone: true,
  imports: [],
  template: `
    <div
      class="toast-container position-fixed bottom-0 end-0 p-3"
      aria-live="polite"
      aria-atomic="false"
    >
      @for (toast of toasts.toasts(); track toast.id) {
        <div
          class="toast show mb-2"
          [class.text-bg-danger]="toast.kind === 'error'"
          [class.text-bg-success]="toast.kind === 'success'"
          [class.text-bg-secondary]="toast.kind === 'info'"
          role="status"
        >
          <div class="d-flex">
            <div class="toast-body">{{ toast.text }}</div>
            <button
              type="button"
              class="btn-close me-2 m-auto"
              [class.btn-close-white]="toast.kind !== 'info'"
              aria-label="Cerrar"
              i18n-aria-label="@@toast.close"
              (click)="toasts.dismiss(toast.id)"
            ></button>
          </div>
        </div>
      }
    </div>
  `,
})
export class Toasts {
  constructor(protected readonly toasts: ToastService) {}
}
