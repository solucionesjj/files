import { Injectable } from '@angular/core';
import { loadTranslations } from '@angular/localize';
import { AppLocale, DEFAULT_LOCALE, EN_TRANSLATIONS, SUPPORTED_LOCALES } from './translations';

const STORAGE_KEY = 'files.locale';

/** Reads the persisted locale (Spanish is the default), safe to call before bootstrap. */
export function readStoredLocale(): AppLocale {
  return localStorage.getItem(STORAGE_KEY) === 'en-US' ? 'en-US' : DEFAULT_LOCALE;
}

/**
 * Applies the persisted locale before the application bootstraps: sets the document language and
 * loads the English message catalog so `$localize` messages render in the selected language.
 */
export function applyLocale(): void {
  const locale = readStoredLocale();
  document.documentElement.lang = locale;
  if (locale === 'en-US') {
    loadTranslations(EN_TRANSLATIONS);
  }
}

@Injectable({ providedIn: 'root' })
export class LocaleService {
  readonly current: AppLocale;
  readonly supportedLocales: readonly AppLocale[] = SUPPORTED_LOCALES;

  constructor() {
    this.current = readStoredLocale();
  }

  setLocale(locale: AppLocale): void {
    localStorage.setItem(STORAGE_KEY, locale);
    window.location.reload();
  }
}
