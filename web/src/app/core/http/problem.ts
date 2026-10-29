/**
 * RFC 9457 problem model surfaced by the Files API. The API always returns a stable `code` (plus
 * `detail` and a `traceId`); the UI branches on `code`, never on `title`.
 */
export interface Problem {
  status?: number;
  title?: string;
  detail?: string;
  type?: string;
  code?: string;
  traceId?: string;
  retryable?: boolean;
}

/** Attempts to read a Problem out of an Angular `HttpErrorResponse` or a fetch `Response`. */
export async function toProblem(error: unknown): Promise<Problem | null> {
  if (!error || typeof error !== 'object') {
    return null;
  }

  // Angular HttpClient rejects with HttpErrorResponse whose `.error` holds the parsed JSON body.
  const candidate = error as { error?: unknown };
  if (candidate.error && typeof candidate.error === 'object') {
    const body = candidate.error as Problem;
    if (typeof body.code === 'string') {
      return body;
    }
  }

  if (error instanceof Response) {
    try {
      const body = (await error.json()) as Problem;
      if (body && typeof body.code === 'string') {
        return body;
      }
    } catch {
      // Non-JSON response; treat as an unknown error.
    }
  }

  return null;
}

/** Maps a problem code to a stable i18n message key for the toast/error surface. */
export function problemMessageKey(code: string | undefined): string {
  switch (code) {
    case 'file_not_found':
      return 'errors.fileNotFound';
    case 'file_unavailable':
      return 'errors.fileUnavailable';
    case 'upload_session_not_found':
      return 'errors.fileNotFound';
    case 'file_too_large':
      return 'errors.fileTooLarge';
    case 'unsupported_content_type':
      return 'errors.unsupportedContentType';
    case 'invalid_filename':
      return 'errors.invalidFilename';
    case 'retention_exceeds_maximum':
      return 'errors.retentionExceedsMaximum';
    case 'retention_reason_required':
      return 'errors.retentionReasonRequired';
    case 'retention_not_allowed':
      return 'errors.retentionNotAllowed';
    case 'stale_version':
      return 'errors.staleVersion';
    case 'legal_hold_active':
      return 'errors.legalHoldActive';
    case 'grant_expired':
    case 'grant_consumed':
    case 'grant_not_found':
      return 'errors.grantInvalid';
    case 'link_not_found':
      return 'errors.linkInvalid';
    case 'public_links_disabled':
      return 'errors.publicLinksDisabled';
    case 'link_limit_reached':
      return 'errors.linkLimitReached';
    case 'staging_capacity_exceeded':
      return 'errors.stagingCapacity';
    case 'chunk_conflict':
    case 'chunk_out_of_range':
    case 'chunk_checksum_mismatch':
    case 'upload_checksum_mismatch':
      return 'errors.uploadCorrupted';
    case 'rate_limited':
    case 'too_many_attempts':
      return 'errors.rateLimited';
    case 'forbidden':
    case 'insufficient_permission':
      return 'errors.forbidden';
    case 'invalid_token':
    case 'token_expired':
      return 'errors.sessionExpired';
    default:
      return 'errors.generic';
  }
}

/** Localized, user-facing message for a problem (Spanish source, English via the catalog). */
export function problemMessage(problem: Problem | null): string {
  switch (problemMessageKey(problem?.code)) {
    case 'errors.fileNotFound':
      return $localize`:@@errors.fileNotFound:Archivo no encontrado.`;
    case 'errors.fileUnavailable':
      return $localize`:@@errors.fileUnavailable:El archivo no está disponible para descargar.`;
    case 'errors.fileTooLarge':
      return $localize`:@@errors.fileTooLarge:El archivo supera el tamaño máximo permitido.`;
    case 'errors.unsupportedContentType':
      return $localize`:@@errors.unsupportedContentType:Este tipo de archivo no está permitido.`;
    case 'errors.invalidFilename':
      return $localize`:@@errors.invalidFilename:El nombre del archivo no es válido.`;
    case 'errors.retentionExceedsMaximum':
      return $localize`:@@errors.retentionExceedsMaximum:La retención supera el máximo permitido.`;
    case 'errors.retentionReasonRequired':
      return $localize`:@@errors.retentionReasonRequired:Se requiere un motivo para ampliar la retención.`;
    case 'errors.retentionNotAllowed':
      return $localize`:@@errors.retentionNotAllowed:La retención indefinida no está permitida para archivos del portal.`;
    case 'errors.staleVersion':
      return $localize`:@@errors.staleVersion:El archivo cambió en otro lugar. Recarga e inténtalo de nuevo.`;
    case 'errors.legalHoldActive':
      return $localize`:@@errors.legalHoldActive:El archivo está bajo retención legal y no puede modificarse ni eliminarse.`;
    case 'errors.grantInvalid':
      return $localize`:@@errors.grantInvalid:El enlace de descarga expiró o ya se usó. Intenta descargar de nuevo.`;
    case 'errors.linkInvalid':
      return $localize`:@@errors.linkInvalid:El enlace no es válido o ya no está disponible.`;
    case 'errors.publicLinksDisabled':
      return $localize`:@@errors.publicLinksDisabled:Los enlaces públicos están deshabilitados.`;
    case 'errors.linkLimitReached':
      return $localize`:@@errors.linkLimitReached:Se alcanzó el límite de enlaces activos.`;
    case 'errors.stagingCapacity':
      return $localize`:@@errors.stagingCapacity:No hay espacio de almacenamiento temporal disponible.`;
    case 'errors.uploadCorrupted':
      return $localize`:@@errors.uploadCorrupted:El fragmento no coincide con la suma de verificación.`;
    case 'errors.rateLimited':
      return $localize`:@@errors.rateLimited:Demasiadas solicitudes. Inténtalo de nuevo más tarde.`;
    case 'errors.forbidden':
      return $localize`:@@errors.forbidden:No tienes permiso para realizar esta acción.`;
    case 'errors.sessionExpired':
      return $localize`:@@errors.sessionExpired:Tu sesión expiró. Vuelve a iniciar sesión.`;
    default:
      return $localize`:@@errors.generic:Se produjo un error inesperado.`;
  }
}
