import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Attaches a bearer token to API requests only (never third parties) and recovers from a 401 with
 * a forced silent renew; when renewal is impossible it falls back to an interactive login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const config = inject(RuntimeConfigService);
  const apiBaseUrl = config.config().apiBaseUrl;

  if (!apiBaseUrl || !req.url.startsWith(apiBaseUrl)) {
    return next(req);
  }

  return from(auth.acquireToken()).pipe(
    switchMap((token) => next(withToken(req, token))),
    catchError((error: HttpErrorResponse) => {
      if (error.status !== 401) {
        return throwError(() => error);
      }

      return from(auth.renewToken()).pipe(
        switchMap((renewed) => {
          if (renewed) {
            return next(withToken(req, renewed));
          }
          void auth.login();
          return throwError(() => error);
        }),
      );
    }),
  );
};

function withToken(req: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}
