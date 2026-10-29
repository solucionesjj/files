-- Release expired reservations for one link and decrement its reserved counter in a single
-- statement. Only grants still holding a reservation (reserved_slot) in an in-flight state are
-- reclaimed, and each is transitioned to 'expired' so no later path (Complete/Fail/Revoke) can
-- release it a second time (ADR-0005: reservations release on expiry).
WITH expired AS (
    UPDATE download_grants g
       SET state = 'expired', updated_at = now()
     WHERE g.link_id = @LinkId
       AND g.reserved_slot
       AND g.state IN ('issued', 'redeemed')
       AND g.expires_at <= @Now
     RETURNING g.id
)
UPDATE download_links l
   SET reserved_downloads = GREATEST(l.reserved_downloads - (SELECT count(*) FROM expired), 0),
       updated_at = now()
 WHERE l.id = @LinkId;
