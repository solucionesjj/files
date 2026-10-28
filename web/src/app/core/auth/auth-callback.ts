import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Redirect target for Entra (`redirectUri` = `/auth/callback`). Drains the authorization-code
 * redirect, then continues to the file list.
 */
@Component({
  selector: 'app-auth-callback',
  standalone: true,
  template: `
    <div class="container py-5 text-center">
      <div class="spinner-border text-primary" role="status">
        <span class="visually-hidden" i18n="@@app.signingIn">Iniciando sesión…</span>
      </div>
    </div>
  `,
})
export class AuthCallback {
  constructor(auth: AuthService, router: Router) {
    void auth.initialize().then(() => router.navigate(['/files']));
  }
}
