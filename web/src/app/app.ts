import { Component } from '@angular/core';
import { AuthService } from './core/auth/auth.service';
import { LanguageSelector } from './layout/language-selector';

@Component({
  selector: 'app-root',
  imports: [LanguageSelector],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  constructor(protected readonly auth: AuthService) {}
}
