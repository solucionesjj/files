import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { Me } from '../models/file-metadata';
import { AuthService } from '../auth/auth.service';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Loads the current identity and Files-managed roles from `GET /v1/me` (ADR-0014). The role is
 * read here — never from `idTokenClaims.roles` — so a role change applies on the next request.
 */
@Injectable({ providedIn: 'root' })
export class MeService {
  private readonly meSignal = signal<Me | null>(null);
  private loading: Promise<Me | null> | null = null;

  readonly me = this.meSignal.asReadonly();

  constructor(
    private readonly http: HttpClient,
    private readonly auth: AuthService,
    private readonly config: RuntimeConfigService,
  ) {}

  load(): Promise<Me | null> {
    this.loading ??= this.fetch();
    return this.loading;
  }

  roles(): string[] {
    return this.meSignal()?.roles ?? [];
  }

  hasRole(role: string): boolean {
    return this.roles().includes(role);
  }

  private async fetch(): Promise<Me | null> {
    const token = await this.auth.acquireToken();
    if (!token) {
      return null;
    }
    const url = `${this.config.config().apiBaseUrl}/v1/me`;
    const me = await firstValueFrom(this.http.get<Me>(url));
    this.meSignal.set(me);
    return me;
  }
}
