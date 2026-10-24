import { Component } from '@angular/core';
import { LocaleService } from '../core/i18n/locale.service';
import { AppLocale } from '../core/i18n/translations';

/** Spanish/English selector; the choice is persisted and applied on reload. */
@Component({
  selector: 'app-language-selector',
  imports: [],
  template: `
    <div class="btn-group" role="group" aria-label="Idioma / Language">
      @for (locale of localeService.supportedLocales; track locale) {
        <button
          type="button"
          class="btn btn-sm"
          [class.btn-outline-primary]="locale === localeService.current"
          [class.btn-outline-secondary]="locale !== localeService.current"
          (click)="select(locale)"
        >
          {{ label(locale) }}
        </button>
      }
    </div>
  `,
})
export class LanguageSelector {
  constructor(protected readonly localeService: LocaleService) {}

  protected label(locale: AppLocale): string {
    return locale === 'es-CO' ? 'Español' : 'English';
  }

  protected select(locale: AppLocale): void {
    if (locale !== this.localeService.current) {
      this.localeService.setLocale(locale);
    }
  }
}
