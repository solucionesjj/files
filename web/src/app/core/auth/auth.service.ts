import { Injectable, computed } from '@angular/core';
import type { IPublicClientApplication } from '@azure/msal-browser';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * MSAL prepared for Entra ID (Authorization Code + PKCE). S3/S6 wires the real tenant/app
 * registrations through /assets/config.json and adds MsalGuard/MsalInterceptor; until then the
 * service stays inert when no clientId is configured so the shell still boots. The MSAL library is
 * loaded lazily (only on first sign-in) to keep it out of the initial shell bundle.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private instance: IPublicClientApplication | null = null;
  private loading: Promise<IPublicClientApplication> | null = null;

  readonly isConfigured = computed(() => {
    const { tenantId, clientId } = this.config.config();
    return Boolean(tenantId && clientId);
  });

  constructor(private readonly config: RuntimeConfigService) {
    void this.config.load();
  }

  async login(): Promise<void> {
    const instance = await this.instanceForLogin();
    if (instance) {
      await instance.loginRedirect();
    }
  }

  async logout(): Promise<void> {
    if (this.instance) {
      await this.instance.logoutRedirect();
    }
  }

  private async instanceForLogin(): Promise<IPublicClientApplication | null> {
    const { tenantId, clientId, redirectUri } = this.config.config();
    if (!tenantId || !clientId) {
      return null;
    }

    this.loading ??= this.createInstance(clientId, tenantId, redirectUri);
    this.instance = await this.loading;
    return this.instance;
  }

  private async createInstance(
    clientId: string,
    tenantId: string,
    redirectUri: string,
  ): Promise<IPublicClientApplication> {
    const { PublicClientApplication } = await import('@azure/msal-browser');
    return new PublicClientApplication({
      auth: {
        clientId,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri,
      },
      cache: {
        cacheLocation: 'sessionStorage',
      },
    });
  }
}
