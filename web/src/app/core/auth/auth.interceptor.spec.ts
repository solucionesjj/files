import { HttpErrorResponse, HttpRequest, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { RuntimeConfigService } from '../config/runtime-config.service';

describe('authInterceptor', () => {
  let auth: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['acquireToken', 'renewToken', 'login']);
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: RuntimeConfigService, useValue: { config: () => ({ apiBaseUrl: 'http://api' }) } },
      ],
    });
  });

  it('attaches a bearer token to API-origin requests', async () => {
    auth.acquireToken.and.resolveTo('token-1');

    const request = await TestBed.runInInjectionContext(async () => {
      const req = new HttpRequest('GET', 'http://api/v1/files');
      const next = jasmine.createSpy('next').and.returnValue(of(new HttpResponse({ status: 200 })));
      await firstValueFrom(authInterceptor(req, next));
      return next.calls.mostRecent().args[0] as HttpRequest<unknown>;
    });

    expect(request.headers.get('Authorization')).toBe('Bearer token-1');
  });

  it('leaves third-party requests untokenized', async () => {
    auth.acquireToken.and.resolveTo('token-1');

    await TestBed.runInInjectionContext(async () => {
      const req = new HttpRequest('GET', 'https://login.microsoftonline.com/x');
      const next = jasmine.createSpy('next').and.returnValue(of(new HttpResponse({ status: 200 })));
      await firstValueFrom(authInterceptor(req, next));
      const forwarded = next.calls.mostRecent().args[0] as HttpRequest<unknown>;
      expect(forwarded.headers.has('Authorization')).toBeFalse();
    });
  });

  it('renews the token and retries once on a 401', async () => {
    auth.acquireToken.and.resolveTo('token-1');
    auth.renewToken.and.resolveTo('renewed-token');

    await TestBed.runInInjectionContext(async () => {
      const req = new HttpRequest('GET', 'http://api/v1/files');
      let calls = 0;
      const next = jasmine.createSpy('next').and.callFake(() => {
        calls++;
        return calls === 1
          ? throwError(() => new HttpErrorResponse({ status: 401 }))
          : of(new HttpResponse({ status: 200 }));
      });

      await firstValueFrom(authInterceptor(req, next));

      const retried = next.calls.mostRecent().args[0] as HttpRequest<unknown>;
      expect(retried.headers.get('Authorization')).toBe('Bearer renewed-token');
      expect(next).toHaveBeenCalledTimes(2);
    });
  });

  it('falls back to login when renewal cannot produce a token', async () => {
    auth.acquireToken.and.resolveTo('token-1');
    auth.renewToken.and.resolveTo(null);

    await TestBed.runInInjectionContext(async () => {
      const req = new HttpRequest('GET', 'http://api/v1/files');
      const next = jasmine.createSpy('next').and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 401 })),
      );

      await expectAsync(firstValueFrom(authInterceptor(req, next))).toBeRejected();
      expect(auth.login).toHaveBeenCalled();
    });
  });
});
