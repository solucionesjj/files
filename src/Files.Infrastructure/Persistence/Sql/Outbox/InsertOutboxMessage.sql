-- Transactional outbox insert. Executed through Dapper inside the ambient unit-of-work
-- transaction; `id`, `state`, `attempts`, `available_at`, and `created_at` use database defaults.
INSERT INTO outbox_messages (event_type, aggregate_type, aggregate_id, payload)
VALUES (@EventType, @AggregateType, @AggregateId, @Payload::jsonb);
