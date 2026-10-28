import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { AuthService } from './auth.service';

/**
 * Requires a signed-in Entra account. Drains the MSAL redirect (token acquisition happens on the
 * `/auth/callback` route), and redirects to Entra when no account is present.
 */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  await auth.initialize();
  if (auth.activeAccount()) {
    return true;
  }
  await auth.login();
  return false;
};
