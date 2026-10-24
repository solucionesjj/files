export type AppLocale = 'es-CO' | 'en-US';

export const DEFAULT_LOCALE: AppLocale = 'es-CO';
export const SUPPORTED_LOCALES: readonly AppLocale[] = ['es-CO', 'en-US'];

/**
 * English overrides for the Spanish source-language messages marked with `i18n="@@..."` in the
 * templates. Loaded at runtime by the locale service when English is selected. Spanish (the source
 * language and default) needs no catalog.
 */
export const EN_TRANSLATIONS: Record<string, string> = {
  'app.signIn': 'Sign in',
  'app.tagline': 'Secure file storage, transfer, and integration platform',
  'app.authNotConfigured': 'Authentication is not configured yet (placeholder Entra credentials).',
};
