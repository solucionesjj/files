/**
 * OpenAPI-derived DTOs for the portal file endpoints. Field names match the API's JSON casing
 * (camelCase). These are maintained manually from `docs/04-api/openapi/files-api.v1.yaml` and
 * type-checked by the API client's usage; keep them in sync when the contract changes.
 */

export type FileStatus =
  | 'pending_upload'
  | 'processing'
  | 'available'
  | 'quarantined'
  | 'expired'
  | 'pending_deletion'
  | 'deleted'
  | 'upload_failed';

export type ScanStatus = 'not_scanned' | 'pending' | 'clean' | 'infected' | 'failed';

export type AccessMode = 'owner_only' | 'private_link' | 'public_link' | 'integration_only';

export type RetentionMode = 'finite' | 'indefinite';

export interface FileRetention {
  mode: RetentionMode;
  days: number | null;
  expiresAt: string | null;
  reason: string | null;
}

export interface FileDownloadStats {
  count: number;
  lastAt: string | null;
}

export interface FileOwner {
  type: 'human' | 'integration';
  displayName: string | null;
}

export interface FileMetadata {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  status: FileStatus;
  accessMode: AccessMode;
  retention: FileRetention;
  tags: string[];
  downloads: FileDownloadStats;
  scanStatus: ScanStatus;
  legalHold: boolean;
  legalHoldReason: string | null;
  legalHoldReviewAt: string | null;
  owner: FileOwner;
  quarantineReason: string | null;
  createdAt: string;
  updatedAt: string;
  availableAt: string | null;
  deletedAt: string | null;
  version: number;
  etag: string;
}

export interface FileList {
  items: FileMetadata[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface FileListQuery {
  q?: string;
  tag?: string[];
  status?: FileStatus[];
  sort?: string;
  expiringWithinDays?: number;
  expiresBefore?: string;
  expiresAfter?: string;
  cursor?: string;
  limit?: number;
}

export interface Me {
  userId: string;
  tenantId: string;
  email: string | null;
  displayName: string;
  roles: string[];
  permissions: string[];
}

export interface CreateUploadSessionRequest {
  fileName: string;
  sizeBytes: number;
  contentType: string;
  checksumSha256?: string;
  retention?: { mode: 'finite'; days: number; reason?: string };
  tags?: string[];
}

export interface UploadSession {
  fileId: string;
  uploadSessionId: string;
  chunkSizeBytes: number;
  chunkCount: number;
  expiresAt: string;
  receivedBytes: number;
}

export interface UploadSessionStatus {
  fileId: string;
  uploadSessionId: string;
  status: string;
  chunkSizeBytes: number;
  chunkCount: number;
  receivedBytes: number;
  chunksReceived: number[];
  expiresAt: string;
  lastError: string | null;
}

/** PATCH /v1/portal/files/{id} — rename / retag / change retention (at least one field). */
export interface UpdateFileRequest {
  fileName?: string;
  retention?: { mode: 'finite'; days: number; reason?: string } | null;
  tags?: string[];
}

/** POST /v1/portal/files/{id}/downloads — a 120 s single-use owner download grant. */
export interface DownloadGrant {
  grantToken: string;
  expiresAt: string;
  downloadUrl: string;
}
