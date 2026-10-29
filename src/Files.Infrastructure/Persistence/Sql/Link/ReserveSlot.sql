-- Atomically reserve one download slot on an active, unexpired link with remaining capacity
-- (ADR-0005). Returns the link id when a slot was reserved, otherwise no row. The preceding
-- ReleaseExpiredReservations must run first in the same transaction so the capacity check sees
-- released slots.
UPDATE download_links l
   SET reserved_downloads = l.reserved_downloads + 1,
       updated_at = now()
 WHERE l.id = @LinkId
   AND l.status = 'active'
   AND (l.expires_at IS NULL OR l.expires_at > @Now)
   AND (l.max_downloads IS NULL OR l.successful_downloads + l.reserved_downloads < l.max_downloads)
RETURNING l.id;
