import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth/auth.service';
import { MeService } from './core/api/me.service';
import { ToastService } from './core/http/toast.service';
import { LanguageSelector } from './layout/language-selector';
import { Toasts } from './layout/toasts';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, LanguageSelector, Toasts],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly meService = inject(MeService);
  private readonly toasts = inject(ToastService);

  readonly displayName = computed(() => this.meService.me()?.displayName ?? '');
  readonly role = computed(() => this.meService.me()?.roles[0] ?? '');

  roleLabel(): string {
    return this.role() || $localize`:@@nav.roleUnknown:Rol`;
  }

  constructor() {
    void this.loadIdentity();
  }

  private async loadIdentity(): Promise<void> {
    await this.auth.initialize();
    if (this.auth.activeAccount()) {
      try {
        await this.meService.load();
      } catch {
        this.toasts.errorFromProblem(null);
      }
    }
  }

  logout(): void {
    void this.auth.logout();
  }
}
