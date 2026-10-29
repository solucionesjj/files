-- Revoke a link's outstanding grants (FR-LINK-006/013): any issued/redeemed grant becomes invalid
-- immediately, and every reservation it still holds is released. The same guard (reserved_slot,
-- in-flight state) ensures a grant's slot is released exactly once.
WITH released AS (
    UPDATE download_grants g
       SET state = 'failed', updated_at = now()
     WHERE g.link_id = @LinkId
       AND g.state IN ('issued', 'redeemed')
       AND g.reserved_slot
     RETURNING g.id
)
UPDATE download_links l
   SET reserved_downloads = GREATEST(l.reserved_downloads - (SELECT count(*) FROM released), 0),
       updated_at = now()
 WHERE l.id = @LinkId;
