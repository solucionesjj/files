import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { applyLocale } from './app/core/i18n/locale.service';

applyLocale();

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
