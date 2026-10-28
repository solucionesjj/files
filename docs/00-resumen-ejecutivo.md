# Files — Resumen ejecutivo (ES)

> Documento de orientación para el equipo. La documentación técnica normative está en inglés
> (igual que la especificación de origen); la interfaz del producto será en español. Este resumen
> no reemplaza los documentos enlazados.

**Fecha:** 25 de octubre de 2026 · **Estado:** diseño completo; **M1 (S1–S3) implementado**; infraestructura preexistente (sin Terraform).

## 1. Qué es Files

Una plataforma centralizada para almacenar, administrar y distribuir archivos de forma segura:

- **Portal** para personas (Angular + Bootstrap, autenticación con Microsoft Entra ID).
- **API** para sistemas empresariales (identidades máquina, sin sesión interactiva).
- **S3 privado, accesible solo por Files**, para el contenido; PostgreSQL 18 para metadatos,
  permisos, retención y auditoría.
- **Una sola VM Ubuntu 24.04 (preexistente)** con Docker Compose, Caddy (TLS) y PostgreSQL 18 en el host.
- **Enlaces públicos** protegidos por un token de 256 bits **más** una clave de 8 caracteres.
- **Retención, notificaciones de vencimiento, borrado físico y auditoría** automatizados.

## 2. Qué se preparó

| Entregable | Ubicación |
|---|---|
| Requisitos funcionales y no funcionales con IDs, prioridades e invariantes | [requirements.md](01-product/requirements.md) |
| 31 historias de usuario con criterios de aceptación (Gherkin) | [user-stories.md](01-product/user-stories.md) |
| Roles, matriz de autorización y personas | [rbac-and-personas.md](01-product/rbac-and-personas.md) |
| Decisiones (D-01…D-40) y preguntas abiertas (OQ-01…OQ-26) | [decisions.md](01-product/decisions.md) · [open-questions.md](01-product/open-questions.md) |
| Arquitectura C4, flujos, despliegue, diseño backend/frontend | [architecture.md](02-architecture/architecture.md) · [backend-design.md](02-architecture/backend-design.md) · [frontend-design.md](02-architecture/frontend-design.md) |
| 13 ADR (decisiones de arquitectura) | [adr/](02-architecture/adr/) |
| Modelo de datos + migraciones ejecutables PostgreSQL 18 | [data-model.md](03-data/data-model.md) · [migrations/](../db/migrations/) |
| Contrato OpenAPI 3.1, convenciones y catálogo de errores | [OpenAPI](04-api/openapi/files-api.v1.yaml) · [convenciones](04-api/api-conventions.md) · [errores](04-api/error-catalog.md) |
| Diseño de Entra ID, identidades máquina y modelo de amenazas STRIDE | [entra-id](05-security/entra-id-authentication.md) · [machine identity](05-security/machine-identity-and-authorization.md) · [threat model](05-security/threat-model.md) |
| Ciclo de vida, carga/descarga de archivos grandes, retención/notificaciones/borrado | [file-lifecycle](06-workflows/file-lifecycle.md) · [upload/download](06-workflows/upload-and-download.md) · [retention](06-workflows/retention-notifications-deletion.md) |
| Estrategia de pruebas | [testing-strategy.md](07-quality/testing-strategy.md) |
| Infraestructura y despliegue (host existente, sin Terraform), rollback, SLOs, costos, runbooks | [operations](08-operations/) |
| Hoja de ruta (M0–M7, sesiones S1–S14), backlog y trazabilidad | [roadmap.md](09-delivery/roadmap.md) · [backlog.md](09-delivery/backlog.md) · [traceability.md](09-delivery/traceability.md) |

Además: guía de trabajo para futuras sesiones ([AGENTS.md](../AGENTS.md)), pila local
(PostgreSQL 18 + LocalStack S3 + Mailpit + OpenTelemetry), CI y scripts de validación, y el
bootstrap del host + despliegue con Compose en `deploy/`.

## 3. Decisiones clave (resumen)

1. **S3 exclusivo de Files:** solo Files accede al bucket (access key estática en
   `/opt/files/.env`); la política del bucket (gestionada externamente) restringe el acceso a esta
   VM. No se emiten URLs prefirmadas.
2. **Cargas 100 % por la API en bloques de 16 MiB**, con área temporal en el disco del host, verificación de
   tamaño/hash y subida multipart desde la API a S3 (reintentable sin re-subir del cliente).
3. **Descargas a través de la API** con concesiones de 120 s y un solo uso; se cuenta solo la
   descarga completada y se puede revocar al instante.
4. **Enlaces públicos:** token de 256 bits en la URL + clave de 8 caracteres fuera de la URL,
   almacenada como Argon2id, con bloqueo progresivo y monitoreo. 62^8 ≈ 47,6 bits; el bloqueo por
   enlace limita el ataque a ~4×10⁻⁹ de probabilidad anual.
5. **Conteo de descargas:** una concesión (grant) de 120 s y un solo uso; la descarga cuenta al
   completarse; las reservas atómicas hacen cumplir el máximo de descargas concurrentes.
6. **Retención:** `finite` (con fecha) o `indefinite` (sin fecha, nunca una fecha lejana). Los
   archivos de integraciones son indefinidos y privados; solo un administrador puede cambiarlo.
7. **Aislamiento:** un archivo de integración no aparece en búsquedas del portal; el acceso entre
   integraciones requiere una concesión explícita de solo lectura. Los administradores gestionan
   metadatos y políticas, pero **no** acceden al contenido por defecto.
8. **Identidad:** Entra ID (OIDC + PKCE) **solo para personas**; las integraciones usan
   **API keys emitidas por Files**; los roles de usuario viven en Files.
9. **Procesos en segundo plano:** outbox transaccional + Quartz en memoria + *advisory locks* de
   PostgreSQL; jobs idempotentes para notificar, vencer, borrar y reconciliar.
10. **Correo:** buzón Microsoft 365; POP solo para recibir, **SMTP AUTH 587** para enviar (Graph
    opcional). SMTP AUTH confirmado habilitado; la app password queda pendiente de entrega (OQ-01).
11. **Infraestructura:** una VM Ubuntu 24.04 **preexistente** (IP pública, SSH) en `us-east-1` y un
    bucket S3 **preexistente**; no se crea infraestructura (sin Terraform). `files.sacservices.cloud`
    (Network Solutions) apunta a la IP pública; Caddy es la única entrada pública (80/443),
    PostgreSQL no expone puerto, la API/Worker usan una access key estática y los secretos viven en
    `/opt/files/.env` (0600). El desarrollo/pruebas corren 100 % en local (LocalStack).
12. **Fase inicial sin HA:** disponibilidad objetivo 99.5%, RPO ≤24 h (backup nocturno `pg_dump`
    a S3) y RTO ≤4 h; ClamAV escanea antes de subir a S3; el camino de escalamiento a RDS/ECS/ALB
    queda documentado.

## 4. Validaciones ya ejecutadas

- Migraciones `V1` y `V2` aplicadas sobre **PostgreSQL 18.6** real; 27 tablas; restricciones de
  propietario, retención, enlaces y notificaciones verificadas.
- Reserva atómica de descargas probada (máximo 2 → la tercera falla).
- Contrato OpenAPI válido y sin advertencias con Redocly (47 rutas, 59 operaciones).
- Pila local levantada: PostgreSQL 18.6, LocalStack 3.8.1 (bucket privado, sin CORS porque el
  navegador no accede a S3), Mailpit, colector OTel; Flyway aplicó las migraciones.
- Pila de despliegue verificada: `docker compose config` (local y producción); `make validate` sin
  Terraform (la infraestructura ya existe y se gestiona fuera del repo).
- Enlaces internos y consistencia de IDs de requisitos/historias verificados por script.

## 5. Decisiones cerradas y datos operativos pendientes

Las decisiones de producto (OQ-01…OQ-26) están cerradas y los datos operativos se confirmaron el
**2026-10-24**. Para desplegar solo queda recibir los datos marcados en la última columna:

| Tema | Estado | Qué falta |
|---|---|---|
| Correo | Buzón `notificationsagent@ecs-la.com`; POP solo recepción, SMTP AUTH 587 para envío. **SMTP AUTH confirmado habilitado** (2026-10-24). | Recibir la app password fuera del chat y depositarla en `/opt/files/.env`. |
| Nube/S3 | AWS `us-east-1`. **Una VM Ubuntu 24.04 existente (IP pública, SSH) + un bucket S3 existente**; no se crea infraestructura (sin Terraform). | Nombre del bucket y la access key estática (`S3__AccessKeyId`/`S3__SecretAccessKey`); la policy del bucket ya restringe el acceso a esta VM. |
| Entra ID | Tenant y app registrations ya existen. | `Entra__TenantId`, `Entra__ApiClientId`, `Entra__PortalClientId`. |
| Graph (ACL) | Permisos `User.ReadBasic.All`, `Group.ReadBasic.All`, `GroupMember.Read.All` aprobados con consentimiento de admin. | Confirmar la concesión antes de M4. |
| Dominio/TLS | `files.sacservices.cloud` en Network Solutions; Caddy/Let's Encrypt. El equipo de infra crea el registro `A` hacia la IP pública de la VM. | Confirmación de que el registro existe. |
| Acceso al host | Administración por **SSH vía IP pública**; el firewall/security group ya está configurado y **no se toca**. | Nada (clave SSH entregada por el equipo de infra). |
| Entornos | **Solo producción**; sin staging separado (2026-10-24). El gate 9 se relaja a un ensayo sobre instancia desechable. | Ejecutar y registrar el ensayo de migraciones/rollback antes de S14. |
| Despliegue | Manual, siguiendo `deploy/README.md`. | — |
| Retención | 30 días por defecto, 365 máximo, nota obligatoria para ampliar; legal hold en v1. | Nada; implementar. |
| Enlaces | Públicos 15 días, privados 30 días (admin); ACL por usuarios/grupos o todo el tenant. | Nada; implementar. |
| Escaneo | ClamAV antes de S3; host de **≥8 GiB / ≥2 vCPU**. | Nada; el host debe ser x86_64. |
| Presupuesto | ~US$110–125/mes producción; ~US$26/mes dev sin escaneo. **Aprobado** (2026-10-24). | Activar alertas de presupuesto en AWS. |
| Alcance | Sin carpetas, sin vista previa, sin acceso admin al contenido, sin API keys, UI español/inglés; **legal hold incluido**. | Nada; está en el alcance. |

## 6. Próximos pasos

1. **S1 (M0):** ejecutar `scripts/bootstrap-solution.sh`, crear la solución .NET y el workspace
   Angular, endpoints de salud, validación de configuración, OpenTelemetry y CI.
2. **S2–S3 (M1):** persistencia EF Core, auditoría/outbox, autenticación Entra, administración de
   integraciones y vinculación de apps Entra (sin API keys).
3. **S4–S5 (M2):** adaptador S3, almacenamiento temporal en EBS, endpoints de chunks, subida
   multipart desde la API, verificación, reserva de disco y limpieza.
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
OTel `localhost:4317/4318`. El navegador nunca habla con S3; no se configura CORS. El API y el
portal aún no están generados; S1 los crea.
