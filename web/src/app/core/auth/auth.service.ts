import { Injectable, computed, signal } from '@angular/core';
import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Entra ID sign-in for the portal (ADR-0013/0014). The MSAL `PublicClientApplication` is created
 * lazily on first use so it stays out of the initial shell bundle; tokens are cached in
 * `sessionStorage` (never `localStorage`). Roles are NOT read from the token — the shell loads them
 * from `GET /v1/me` (see MeService); this service only proves identity and supplies access tokens.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private instance: IPublicClientApplication | null = null;
  private initializing: Promise<void> | null = null;

  private readonly accountSignal = signal<AccountInfo | null>(null);

  readonly activeAccount = this.accountSignal.asReadonly();

  readonly isConfigured = computed(() => {
    const { tenantId, clientId } = this.config.config();
    return Boolean(tenantId && clientId);
  });

  constructor(private readonly config: RuntimeConfigService) {
    void this.config.load();
  }

  /** Creates the client and drains any pending redirect (idempotent, safe to await repeatedly). */
  async initialize(): Promise<void> {
    // Wait for the runtime config so the Entra tenant/client IDs are available before we decide
    // whether authentication is configured (otherwise a fast first call sees the fallback config).
    await this.config.load();
    if (!this.isConfigured()) {
      return;
    }
    this.initializing ??= this.doInitialize();
    await this.initializing;
  }

  private async doInitialize(): Promise<void> {
    const instance = await this.instanceForLogin();
    if (!instance) {
      return;
    }

    // MSAL v5 requires an explicit initialize() before redirect/token APIs.
    await instance.initialize();

    try {
      const result = await instance.handleRedirectPromise();
      if (result?.account) {
        this.accountSignal.set(result.account);
        return;
      }
    } catch {
      // Ignore redirect-processing failures; the guard drives a fresh login below.
    }

    const accounts = instance.getAllAccounts();
    this.accountSignal.set(accounts.length > 0 ? accounts[0] : null);
  }

  /** Acquires an access token for the API (or the given scopes), or null when a login is needed. */
  async acquireToken(scopes?: string[]): Promise<string | null> {
    await this.initialize();
    const instance = this.instance;
    const account = this.accountSignal();
    if (!instance || !account) {
      return null;
    }

    const effectiveScopes = scopes?.length ? scopes : [this.config.config().apiScope];
    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    try {
      const result = await instance.acquireTokenSilent({ scopes: effectiveScopes, account });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await instance.loginRedirect({ scopes: effectiveScopes });
        return null;
      }
      throw error;
    }
  }

  /** Re-acquires a token with a forced refresh (used on a 401 to recover from expiry). */
  async renewToken(): Promise<string | null> {
    await this.initialize();
    const instance = this.instance;
    const account = this.accountSignal();
    if (!instance || !account) {
      return null;
    }

    const { InteractionRequiredAuthError } = await import('@azure/msal-browser');
    try {
      const result = await instance.acquireTokenSilent({
        scopes: [this.config.config().apiScope],
        account,
        forceRefresh: true,
      });
      return result.accessToken;
    } catch (error) {
      if (error instanceof InteractionRequiredAuthError) {
        await instance.loginRedirect({ scopes: [this.config.config().apiScope] });
      }
      return null;
    }
  }

  async login(): Promise<void> {
    const instance = await this.instanceForLogin();
    if (instance) {
      await instance.loginRedirect();
    }
  }

  async logout(): Promise<void> {
    const instance = await this.instanceForLogin();
    if (instance) {
      await instance.logoutRedirect({ postLogoutRedirectUri: window.location.origin });
    }
  }

  private async instanceForLogin(): Promise<IPublicClientApplication | null> {
    const { tenantId, clientId, redirectUri } = this.config.config();
    if (!tenantId || !clientId) {
      return null;
    }

    this.instance ??= await this.createInstance(clientId, tenantId, redirectUri);
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
        postLogoutRedirectUri: window.location.origin,
      },
      cache: {
        cacheLocation: 'sessionStorage',
      },
    });
  }
}
