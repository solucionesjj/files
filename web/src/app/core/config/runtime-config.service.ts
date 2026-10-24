import { Injectable, signal } from '@angular/core';

/**
 * Runtime, non-secret front-end configuration. In production Caddy serves this asset with the
 * correct Entra tenant/app IDs; the values here are safe local placeholders and never contain
 * secrets.
 */
export interface RuntimeConfig {
  environment: string;
  apiBaseUrl: string;
  tenantId: string;
  clientId: string;
  apiScope: string;
  redirectUri: string;
  features: { publicLinks: boolean };
}

const FALLBACK_CONFIG: RuntimeConfig = {
  environment: 'local',
  apiBaseUrl: 'http://localhost:8080',
  tenantId: '',
  clientId: '',
  apiScope: '',
  redirectUri: 'http://localhost:4200/auth/callback',
  features: { publicLinks: true },
};

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  readonly config = signal<RuntimeConfig>(FALLBACK_CONFIG);

  async load(): Promise<RuntimeConfig> {
    try {
      const response = await fetch('/assets/config.json', { cache: 'no-store' });
      if (response.ok) {
        const loaded = (await response.json()) as Partial<RuntimeConfig>;
        this.config.set({
          ...FALLBACK_CONFIG,
          ...loaded,
          features: { ...FALLBACK_CONFIG.features, ...loaded.features },
        });
      }
    } catch {
      // Keep the fallback configuration when the asset is unavailable (e.g. in unit tests).
    }
    return this.config();
  }
}
