import { Injectable } from '@angular/core';

export interface SumoTelemetry {
  // Tokenized per-env (__SumoRumIsEnabled__) and replaced at deploy, so it can
  // arrive as a string. Coerced to bool in setupTelemetry (see below).
  isEnabled: boolean | string;
  collectionSourceUrl: string;
  applicationName: string;
  serviceName: string;
  propagateTraceHeaderCorsUrls: string[];
  samplingProbability: number;
  collectSessionId: boolean;
  deploymentEnvironment: string;
  deploymentRegion: string;
  deploymentHostType: string;
}

@Injectable({
  providedIn: 'root'
})
export class TelemetryService {
  private readonly sumoUrl = 'assets/config/sumo-telemetry.json';
  private configPromise: Promise<SumoTelemetry> | null = null;
  private initialized = false;

  // fetch(), not HttpClient: constructed outside DI in main.ts, and HttpXhrBackend
  // requires an injection context under Angular 21 (NG0203).
  public getSumoConfig(): Promise<SumoTelemetry> {
    if (!this.configPromise) {
      this.configPromise = fetch(this.sumoUrl).then((res) => {
        if (!res.ok) {
          throw new Error(`Failed to load telemetry config: ${res.status} ${res.statusText}`);
        }
        return res.json() as Promise<SumoTelemetry>;
      });
    }
    return this.configPromise;
  }

  public setupTelemetry(): Promise<void> {
    return this.getSumoConfig().then(async (config) => {
      // A deploy-time .token swap delivers isEnabled as a string; unreplaced tokens coerce to false.
      const enabled = config?.isEnabled === true || String(config?.isEnabled).toLowerCase() === 'true';
      if (!enabled || this.initialized) {
        return;
      }
      this.initialized = true;
      // Dynamic import so the OTel web SDK stays out of the initial bundle (it used to ride the
      // lazy `@sumologic/opentelemetry-rum` chunk) and is only fetched when telemetry is enabled.
      const { setupTelemetry } = await import('./telemetry-setup');
      setupTelemetry(config);
    }).catch((err) => {
      console.warn('[App] telemetry init failed', err);
    });
  }
}
