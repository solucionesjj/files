/**
 * OpenAPI-derived DTOs for the portal/public link endpoints. Field names match the API's JSON
 * casing (camelCase), maintained manually from `docs/04-api/openapi/files-api.v1.yaml`.
 */

export type LinkStatus = 'active' | 'revoked' | 'expired';
export type LinkAccessMode = 'public';

export interface LinkCreator {
  type: 'user' | 'admin';
  displayName: string | null;
}

export interface Link {
  id: string;
  fileId: string;
  accessMode: LinkAccessMode;
  status: LinkStatus;
  createdAt: string;
  expiresAt: string | null;
  maxDownloads: number | null;
  successfulDownloads: number;
  reservedDownloads: number;
  revokedAt: string | null;
  createdBy: LinkCreator;
}

/** Returned only at creation; `downloadKey` is shown exactly once. */
export interface LinkCreated extends Link {
  url: string;
  token: string;
  downloadKey: string;
}

export interface LinkList {
  items: Link[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface LinkPublicInfo {
  fileName: string | null;
  sizeBytes: number | null;
  contentType: string | null;
  requiresKey: boolean;
  requiresAuthentication: boolean;
  expiresAt: string | null;
  remainingDownloads: number | null;
  ownerDisplayName: string | null;
}

export interface DownloadEvent {
  id: string;
  fileId: string | null;
  linkId: string | null;
  actorType: 'user' | 'integration' | 'anonymous' | 'system';
  actorId: string | null;
  source: 'owner' | 'private_link' | 'public_link' | 'integration';
  result: 'authorized' | 'denied' | 'completed' | 'failed';
  reasonCode: string | null;
  bytesSent: number;
  traceId: string | null;
  occurredAt: string;
}

export interface LinkStats {
  linkId: string;
  successfulDownloads: number;
  reservedDownloads: number;
  failedAttempts: number;
  lastDownloadAt: string | null;
  recentEvents: DownloadEvent[];
}

/** POST /v1/portal/files/{id}/links — public links only. */
export interface CreateLinkRequest {
  accessMode: 'public';
  maxDownloads?: number;
}

/** POST /v1/public/download-links/{token}/downloads — 8-char case-sensitive key. */
export interface RedeemPublicLinkRequest {
  key: string;
}

/** POST /v1/portal/files/{id}/downloads and /v1/public/download-links/{token}/downloads. */
export interface DownloadGrant {
  grantToken: string;
  expiresAt: string;
  downloadUrl: string;
}
