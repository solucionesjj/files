UPDATE files
   SET scan_next_attempt_at = now() + make_interval(secs => @LeaseSeconds)
 WHERE id = ANY(@FileIds);
