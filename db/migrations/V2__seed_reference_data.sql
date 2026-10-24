-- Files — reference data
-- Applied to every environment. No demo users, integrations, or files here.

-- Active global retention policy (defaults per D-08 / OQ-07)
INSERT INTO retention_policies (
    id, default_days, max_days, allow_user_override, allow_indefinite,
    public_links_enabled, notes
) VALUES (
    uuidv7(), 30, 365, true, false, true,
    'Seeded baseline policy. 30 days default, 365-day absolute maximum (OQ-07).'
);

-- Active notification policy
INSERT INTO notification_policies (id, name, intervals_days, template_key)
VALUES (uuidv7(), 'Default expiration reminders', '{7,3,1}', 'file.expiring');

-- Email templates (Spanish default, English fallback)
INSERT INTO email_templates (id, key, locale, subject, body_html, body_text, version)
VALUES
(
    uuidv7(), 'file.expiring', 'es-CO',
    'Tu archivo "{{fileName}}" vence en {{intervalDays}} días',
    $html_es$<!doctype html>
<html lang="es">
<body style="font-family:Arial,Helvetica,sans-serif;color:#212529">
  <p>Hola {{ownerName}},</p>
  <p>El archivo <strong>{{fileName}}</strong> vencerá el <strong>{{expiresAt}}</strong>
     (en {{intervalDays}} días).</p>
  <p>Si aún lo necesitas, ingresa a <a href="{{portalUrl}}">Files</a> y amplía su retención.</p>
  <p>Si no haces nada, el archivo dejará de estar disponible y se eliminará según la política
     de retención.</p>
  <hr>
  <p style="font-size:12px;color:#6c757d">Este mensaje fue enviado automáticamente por Files.
     No respondas a este correo. No contiene datos del archivo ni credenciales de acceso.</p>
</body>
</html>$html_es$,
    $text_es$Hola {{ownerName}},

El archivo "{{fileName}}" vencerá el {{expiresAt}} (en {{intervalDays}} días).
Si aún lo necesitas, ingresa a {{portalUrl}} y amplía su retención.
Si no haces nada, el archivo dejará de estar disponible y se eliminará según la política de retención.

Este mensaje fue enviado automáticamente por Files. No contiene datos del archivo ni credenciales.$text_es$,
    1
),
(
    uuidv7(), 'file.expiring', 'en-US',
    'Your file "{{fileName}}" expires in {{intervalDays}} days',
    $html_en$<!doctype html>
<html lang="en">
<body style="font-family:Arial,Helvetica,sans-serif;color:#212529">
  <p>Hello {{ownerName}},</p>
  <p>The file <strong>{{fileName}}</strong> will expire on <strong>{{expiresAt}}</strong>
     (in {{intervalDays}} days).</p>
  <p>If you still need it, sign in to <a href="{{portalUrl}}">Files</a> and extend its retention.</p>
  <p>Otherwise it will become unavailable and be deleted according to the retention policy.</p>
  <hr>
  <p style="font-size:12px;color:#6c757d">This message was sent automatically by Files.
     Do not reply. It contains no file content or access credentials.</p>
</body>
</html>$html_en$,
    $text_en$Hello {{ownerName}},

The file "{{fileName}}" will expire on {{expiresAt}} (in {{intervalDays}} days).
If you still need it, sign in to {{portalUrl}} and extend its retention.
Otherwise it will become unavailable and be deleted according to the retention policy.

This message was sent automatically by Files. It contains no file content or access credentials.$text_en$,
    1
);

-- Admin-editable limits (DB overrides of non-security defaults)
INSERT INTO platform_settings (key, value, description) VALUES
    ('public_links_enabled', 'true', 'Whether owners may create public download links.'),
    ('max_file_size_bytes', '5368709120', 'Maximum upload size per file in bytes (5 GiB).'),
    ('max_links_per_file', '100', 'Maximum active links per file.'),
    ('max_links_per_user', '1000', 'Maximum active links per user.'),
    ('max_upload_sessions_per_integration', '5', 'Concurrent open upload sessions per integration.'),
    ('max_proxy_upload_bytes', '104857600', 'Maximum size for API-staged uploads (100 MiB).'),
    ('global_storage_quota_bytes', '536870912000', 'Soft storage quota for the deployment (500 GiB).'),
    ('notifications_enabled', 'true', 'Master switch for expiration notifications.'),
    ('public_link_ttl_days', '15', 'Lifetime of newly created public links (admin-defined).'),
    ('private_link_ttl_days', '30', 'Lifetime of newly created private links (admin-defined).');
