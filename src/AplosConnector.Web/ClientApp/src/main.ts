import { enableProdMode, provideZoneChangeDetection } from '@angular/core';
import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';

import { AppModule } from './app/app.module';
import { environment } from './environments/environment';
import { TelemetryService } from './app/services/telemetry.service';


export function getBaseUrl() {
  return document.getElementsByTagName('base')[0].href;
}

const providers = [
  { provide: 'BASE_URL', useFactory: getBaseUrl, deps: [] }
];

// Outside DI (it fetches its config, since HttpClient would throw NG0203 here); started after bootstrap so ZoneContextManager patches a ready Zone.
const telemetryLoader = new TelemetryService();

if (environment.production) {
  enableProdMode();
}

platformBrowserDynamic(providers).bootstrapModule(AppModule, { applicationProviders: [provideZoneChangeDetection()], })
  .then(() => telemetryLoader.setupTelemetry())
  .catch(err => console.error(err));
