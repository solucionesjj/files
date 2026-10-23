# Files — Resumen ejecutivo (ES)

> Documento de orientación para el equipo. La documentación técnica normative está en inglés
> (igual que la especificación de origen); la interfaz del producto será en español. Este resumen
> no reemplaza los documentos enlazados.

**Fecha:** 23 de octubre de 2026 · **Estado:** diseño completo, implementación no iniciada.

## 1. Qué es Files

Una plataforma centralizada para almacenar, administrar y distribuir archivos de forma segura:

- **Portal** para personas (Angular + Bootstrap, autenticación con Microsoft Entra ID).
- **API** para sistemas empresariales (identidades máquina, sin sesión interactiva).
- **S3 privado** para el contenido; PostgreSQL 18 para metadatos, permisos, retención y auditoría.
- **Enlaces públicos** protegidos por un token de 256 bits **más** una clave de 8 caracteres.
- **Retención, notificaciones de vencimiento, borrado físico y auditoría** automatizados.

## 2. Qué se preparó

| Entregable | Ubicación |
|---|---|
| Requisitos funcionales y no funcionales con IDs, prioridades e invariantes | [requirements.md](01-product/requirements.md) |
| 31 historias de usuario con criterios de aceptación (Gherkin) | [user-stories.md](01-product/user-stories.md) |
| Roles, matriz de autorización y personas | [rbac-and-personas.md](01-product/rbac-and-personas.md) |
| Decisiones (D-01…D-29) y preguntas abiertas (OQ-01…OQ-24) | [decisions.md](01-product/decisions.md) · [open-questions.md](01-product/open-questions.md) |
| Arquitectura C4, flujos, despliegue, diseño backend/frontend | [architecture.md](02-architecture/architecture.md) · [backend-design.md](02-architecture/backend-design.md) · [frontend-design.md](02-architecture/frontend-design.md) |
| 13 ADR (decisiones de arquitectura) | [adr/](02-architecture/adr/) |
| Modelo de datos + migraciones ejecutables PostgreSQL 18 | [data-model.md](03-data/data-model.md) · [migrations/](../db/migrations/) |
| Contrato OpenAPI 3.1, convenciones y catálogo de errores | [OpenAPI](04-api/openapi/files-api.v1.yaml) · [convenciones](04-api/api-conventions.md) · [errores](04-api/error-catalog.md) |
| Diseño de Entra ID, identidades máquina y modelo de amenazas STRIDE | [entra-id](05-security/entra-id-authentication.md) · [machine identity](05-security/machine-identity-and-authorization.md) · [threat model](05-security/threat-model.md) |
| Ciclo de vida, carga/descarga de archivos grandes, retención/notificaciones/borrado | [file-lifecycle](06-workflows/file-lifecycle.md) · [upload/download](06-workflows/upload-and-download.md) · [retention](06-workflows/retention-notifications-deletion.md) |
| Estrategia de pruebas | [testing-strategy.md](07-quality/testing-strategy.md) |
| Infraestructura AWS (Terraform), despliegue/rollback, SLOs, costos, runbooks | [operations](08-operations/) |
| Hoja de ruta (M0–M7, sesiones S1–S14), backlog y trazabilidad | [roadmap.md](09-delivery/roadmap.md) · [backlog.md](09-delivery/backlog.md) · [traceability.md](09-delivery/traceability.md) |

Además: guía de trabajo para futuras sesiones ([AGENTS.md](../AGENTS.md)), pila local
(PostgreSQL 18 + LocalStack S3 + Mailpit + OpenTelemetry), CI y scripts de validación, y un esqueleto
de Terraform para AWS.

## 3. Decisiones clave (resumen)

1. **Descargas a través de la API**, no con URLs prefirmadas de S3. Permite contar solo descargas
   completadas, revocar al instante y auditar cada intento. El costo es el egreso de la API.
2. **Carga directa a S3** con multipart prefirmado (hasta 5 GiB por defecto). Los archivos pequeños
   pueden pasar por la API hacia un área temporal (`staging/`) y luego promoverse.
3. **Enlaces públicos:** token de 256 bits en la URL + clave de 8 caracteres fuera de la URL,
   almacenada como Argon2id, con bloqueo progresivo y monitoreo. 62^8 ≈ 47,6 bits; el bloqueo por
   enlace limita el ataque a ~4×10⁻⁹ de probabilidad anual.
4. **Conteo de descargas:** una concesión (grant) de 120 s y un solo uso; la descarga cuenta al
   completarse; las reservas atómicas hacen cumplir el máximo de descargas concurrentes.
5. **Retención:** `finite` (con fecha) o `indefinite` (sin fecha, nunca una fecha lejana). Los
   archivos de integraciones son indefinidos y privados; solo un administrador puede cambiarlo.
6. **Aislamiento:** un archivo de integración no aparece en búsquedas del portal; el acceso entre
   integraciones requiere una concesión explícita de solo lectura. Los administradores gestionan
   metadatos y políticas, pero **no** acceden al contenido por defecto.
7. **Identidad:** Entra ID (OIDC + PKCE) para personas; *client credentials* para máquinas; API
   keys (`flk_...`) solo como compatibilidad, con rotación y hash HMAC.
8. **Procesos en segundo plano:** outbox transaccional + Quartz en memoria + *advisory locks* de
   PostgreSQL; jobs idempotentes para notificar, vencer, borrar y reconciliar.
9. **Correo:** Microsoft Graph `sendMail` (no POP, que no envía); queda pendiente confirmar buzón y
   permiso (OQ-01).
10. **Infraestructura:** AWS ECS Fargate + RDS PostgreSQL 18 + S3 privado + ALB + Terraform;
    secretos sensibles vía Secrets Manager inyectados como variables de entorno.

## 4. Validaciones ya ejecutadas

- Migraciones `V1` y `V2` aplicadas sobre **PostgreSQL 18.6** real; 27 tablas; restricciones de
  propietario, retención, enlaces y notificaciones verificadas.
- Reserva atómica de descargas probada (máximo 2 → la tercera falla).
- Contrato OpenAPI válido y sin advertencias con Redocly (47 rutas, 59 operaciones).
- Pila local levantada: PostgreSQL 18.6, LocalStack 3.8.1 (bucket privado + CORS de subida),
  Mailpit, colector OTel; Flyway aplicó las migraciones; preflight CORS correcto.
- Terraform `fmt` y `validate` correctos (AWS provider 6.70.0); grafo sin ciclos.
- Enlaces internos y consistencia de IDs de requisitos/historias verificados por script.

## 5. Pendientes que requieren decisión

| Tema | Pregunta | Impacto |
|---|---|---|
| Correo | Confirmar Graph `sendMail`, buzón remitente y permiso de aplicación (OQ-01). | Bloquea notificaciones en producción (M6). |
| Nube/región | Confirmar AWS y región; requisitos de residencia (OQ-02). | Afecta IaC, latencia y costo. |
| Retención | Validar 365 días por defecto / 3650 máximo / indefinido solo integraciones (OQ-07). | Política y legal. |
| Dominio/TLS | Dominio, certificado ACM y DNS (OQ-15). | Necesario para el corte a producción. |
| Escaneo de malware | ¿Obligatorio al lanzamiento? (OQ-09). | Seguridad y latencia de disponibilidad. |
| Presupuesto | Costo base estimado US$360–400/mes en producción (OQ-20). | Aprobación financiera. |
| Alcance | Carpetas, vista previa y acceso de soporte al contenido quedan fuera de v1. | Evita desvíos. |

## 6. Próximos pasos

1. **S1 (M0):** ejecutar `scripts/bootstrap-solution.sh`, crear la solución .NET y el workspace
   Angular, endpoints de salud, validación de configuración, OpenTelemetry y CI.
2. **S2–S3 (M1):** persistencia EF Core, auditoría/outbox, autenticación Entra, administración de
   integraciones y API keys.
3. **S4–S5 (M2):** adaptador S3, sesiones de carga, multipart, verificación y limpieza.
4. **S6–S8:** portal, enlaces y descargas públicas.
5. **S9–S14:** API de integración, retención/notificaciones/borrado, endurecimiento y lanzamiento.

El detalle, dependencias y criterios de salida están en la [hoja de ruta](09-delivery/roadmap.md).
La guía para retomar el trabajo en una sesión futura está en [AGENTS.md](../AGENTS.md).

## 7. Cómo iniciar el entorno local

```bash
cp .env.example .env
docker compose up -d
docker compose --profile tools run --rm flyway migrate
make validate
```

Servicios: PostgreSQL `localhost:5432` (`files`/`files`/`files`), LocalStack S3
`http://localhost:4566` (`test`/`test`, bucket `files-local`), Mailpit `http://localhost:8025`,
OTel `localhost:4317/4318`. El API y el portal aún no están generados; S1 los crea.
