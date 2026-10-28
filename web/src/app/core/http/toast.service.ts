import { Injectable, signal } from '@angular/core';
import type { Problem } from '../http/problem';
import { problemMessage } from '../http/problem';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  text: string;
  kind: ToastKind;
}

/** Lightweight toast queue; messages are localized before being pushed. */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly toastsSignal = signal<Toast[]>([]);
  private nextId = 0;

  readonly toasts = this.toastsSignal.asReadonly();

  push(text: string, kind: ToastKind = 'info'): void {
    const id = ++this.nextId;
    this.toastsSignal.update((current) => [...current, { id, text, kind }]);
    setTimeout(() => this.dismiss(id), 6000);
  }

  dismiss(id: number): void {
    this.toastsSignal.update((current) => current.filter((toast) => toast.id !== id));
  }

  success(text: string): void {
    this.push(text, 'success');
  }

  error(text: string): void {
    this.push(text, 'error');
  }

  errorFromProblem(problem: Problem | null): void {
    this.error(problemMessage(problem));
  }
}
