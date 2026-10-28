import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type {
  CreateUploadSessionRequest,
  DownloadGrant,
  FileList,
  FileListQuery,
  FileMetadata,
  UpdateFileRequest,
  UploadSession,
  UploadSessionStatus,
} from '../models/file-metadata';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Typed client for the owner-scoped portal file endpoints. Chunk uploads are performed with XHR
 * (see UploadService) so progress events are available; this client covers the JSON endpoints.
 */
@Injectable({ providedIn: 'root' })
export class FilesApiService {
  constructor(
    private readonly http: HttpClient,
    private readonly config: RuntimeConfigService,
  ) {}

  list(query: FileListQuery): Promise<FileList> {
    let params = new HttpParams();
    if (query.q) params = params.set('q', query.q);
    if (query.sort) params = params.set('sort', query.sort);
    if (query.limit) params = params.set('limit', String(query.limit));
    if (query.cursor) params = params.set('cursor', query.cursor);
    if (query.expiringWithinDays) params = params.set('expiringWithinDays', String(query.expiringWithinDays));
    if (query.expiresBefore) params = params.set('expiresBefore', query.expiresBefore);
    if (query.expiresAfter) params = params.set('expiresAfter', query.expiresAfter);
    for (const tag of query.tag ?? []) params = params.append('tag', tag);
    for (const status of query.status ?? []) params = params.append('status', status);

    return firstValueFrom(this.http.get<FileList>(this.url('/v1/portal/files'), { params }));
  }

  get(fileId: string): Promise<FileMetadata> {
    return firstValueFrom(this.http.get<FileMetadata>(this.url(`/v1/portal/files/${fileId}`)));
  }

  createSession(request: CreateUploadSessionRequest): Promise<UploadSession> {
    return firstValueFrom(this.http.post<UploadSession>(this.url('/v1/portal/upload-sessions'), request));
  }

  getStatus(sessionId: string): Promise<UploadSessionStatus> {
    return firstValueFrom(this.http.get<UploadSessionStatus>(this.url(`/v1/portal/upload-sessions/${sessionId}`)));
  }

  complete(sessionId: string, checksumSha256?: string): Promise<FileMetadata> {
    return firstValueFrom(
      this.http.post<FileMetadata>(this.url(`/v1/portal/upload-sessions/${sessionId}/complete`), {
        checksumSha256,
      }),
    );
  }

  abort(sessionId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url(`/v1/portal/upload-sessions/${sessionId}`)));
  }

  update(fileId: string, request: UpdateFileRequest, etag: string): Promise<FileMetadata> {
    return firstValueFrom(
      this.http.patch<FileMetadata>(this.url(`/v1/portal/files/${fileId}`), request, {
        headers: { 'If-Match': etag },
      }),
    );
  }

  delete(fileId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(this.url(`/v1/portal/files/${fileId}`)));
  }

  createDownloadGrant(fileId: string): Promise<DownloadGrant> {
    return firstValueFrom(
      this.http.post<DownloadGrant>(this.url(`/v1/portal/files/${fileId}/downloads`), null),
    );
  }

  private url(path: string): string {
    return `${this.config.config().apiBaseUrl}${path}`;
  }
}
