import { Attributes } from '@opentelemetry/api';
import { SeverityNumber, logs } from '@opentelemetry/api-logs';
import { BatchLogRecordProcessor, LoggerProvider } from '@opentelemetry/sdk-logs';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-http';
import { ExportResult, W3CTraceContextPropagator } from '@opentelemetry/core';
import { Resource, defaultResource, resourceFromAttributes } from '@opentelemetry/resources';
import { ZoneContextManager } from '@opentelemetry/context-zone';
import {
  BatchSpanProcessor,
  ParentBasedSampler,
  ReadableSpan,
  Span,
  SpanExporter,
  SpanProcessor,
  TraceIdRatioBasedSampler
} from '@opentelemetry/sdk-trace-base';
import { WebTracerProvider, getElementXPath } from '@opentelemetry/sdk-trace-web';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-http';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { XMLHttpRequestInstrumentation } from '@opentelemetry/instrumentation-xml-http-request';
import { UserInteractionInstrumentation } from '@opentelemetry/instrumentation-user-interaction';
import { LongTaskInstrumentation } from '@opentelemetry/instrumentation-long-task';
import { environment } from '../../environments/environment';
import { SumoTelemetry } from './telemetry.service';

// Matches the Sumo RUM session semantics we replaced: a cross-tab cookie that rotates after
// SESSION_TTL_MS of inactivity, surfaced on spans under the same `rum.session_id` key Sumo used.
const SESSION_COOKIE = 'rumSessionId';
const SESSION_TTL_MS = 5 * 60 * 1000;
// Only touch document.cookie this often; between refreshes the in-memory id is reused so we don't
// do a synchronous cookie read+write on every span. Must stay well under SESSION_TTL_MS so the
// cookie keeps getting refreshed within its window during continuous activity.
const SESSION_REFRESH_MS = 30 * 1000;
const SESSION_ATTRIBUTE = 'rum.session_id';
const RUM_SCOPE_NAME = 'sumologic-rum';
const IGNORE_URLS = [/^chrome-extension:\/\//];

// Strip URL query + fragment (plain truncation — also correct for opaque blob:/data: origins) so a query-string secret never reaches telemetry (150354).
// Aplos: the /finish-pex-login/:sessionId route carries a bearer session id in the path, so mask that segment too.
export const scrubUrl = (url: string): string => url.split(/[?#]/)[0].replace(/(\/finish-pex-login\/)[^/?#]+/gi, '$1{sessionId}');

// URL-valued span attrs to scrub — we strip ALL query params (incl. non-secret API params), a security-first default (150354).
const URL_SPAN_ATTRIBUTES = ['location.href', 'http.url', 'url.full', 'url.query'];
// Long-task container_src is the embedding iframe's src (the headless ?jwt= handoff URL); the suffix match also covers longtask.attribution[i] (150354).
const isUrlSpanAttribute = (key: string): boolean => URL_SPAN_ATTRIBUTES.includes(key) || key.endsWith('.container_src');

const readCookie = (name: string): string | undefined => {
  const match = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
  return match ? decodeURIComponent(match[1]) : undefined;
};

const writeCookie = (name: string, value: string, ttlMs: number): void => {
  const expires = new Date(Date.now() + ttlMs).toUTCString();
  document.cookie = `${name}=${encodeURIComponent(value)}; path=/; expires=${expires}; SameSite=Lax`;
};

let cachedSessionId: string | undefined;
let lastSessionRefresh = 0;

// Sumo RUM minted session ids with the SDK's trace-id generator (32 random hex chars), so this
// keeps the same shape. crypto.randomUUID() is deliberately not used: it is undefined outside a
// secure context (plain-http hosts other than localhost), which would throw inside onStart and
// break every span. getRandomValues has no such restriction, and Math.random covers the rest.
const generateSessionId = (): string => {
  const bytes = new Uint8Array(16);
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i++) {
      bytes[i] = Math.floor(Math.random() * 256);
    }
  }
  return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
};

const getSessionId = (): string => {
  const now = Date.now();
  // Fast path: reuse the cached id and skip cookie I/O until the throttle window elapses.
  if (cachedSessionId && now - lastSessionRefresh < SESSION_REFRESH_MS) {
    return cachedSessionId;
  }
  // Re-sync with the cookie: adopt an existing (possibly cross-tab) id, or mint a new one if the
  // cookie expired after SESSION_TTL_MS of inactivity. Refresh its rolling expiry.
  cachedSessionId = readCookie(SESSION_COOKIE) ?? generateSessionId();
  writeCookie(SESSION_COOKIE, cachedSessionId, SESSION_TTL_MS);
  lastSessionRefresh = now;
  return cachedSessionId;
};

/**
 * Stamps the per-span context attributes the Sumo RUM span processor used to add: the current page
 * URL, the document visibility state, and (when enabled) the rolling session id.
 */
class RumEnrichmentSpanProcessor implements SpanProcessor {
  public constructor(private readonly collectSessionId: boolean) { }
  public onStart(span: Span): void {
    span.setAttribute('location.href', scrubUrl(window.location.href));
    // Key kept verbatim from the Sumo wrapper (not snake_cased) so existing Sumo dashboards and
    // queries filtering on it keep working.
    span.setAttribute('document.visibilityState', document.visibilityState);
    if (this.collectSessionId) {
      span.setAttribute(SESSION_ATTRIBUTE, getSessionId());
    }
  }
  public onEnd(): void { }
  public forceFlush(): Promise<void> {
    return Promise.resolve();
  }
  public shutdown(): Promise<void> {
    return Promise.resolve();
  }
}

// `@opentelemetry/instrumentation-user-interaction` calls `zone.get('OT_ZONE_CONTEXT')` inside its
// private `_getCurrentSpan` without checking the receiver, so a non-Zone receiver throws
// `.get is not a function` and surfaces as an uncaught error in RUM (Dashboard #144388). Returning
// no span matches how the library already treats a zone with no active context.
type ZoneSpanLookup = { _getCurrentSpan(zone: unknown): unknown };

const guardZoneContextLookup = (instrumentation: UserInteractionInstrumentation): void => {
  const internals = instrumentation as unknown as Partial<ZoneSpanLookup>;
  const getCurrentSpan = internals._getCurrentSpan;
  // A library upgrade may rename this private. Leaving it unguarded is recoverable; throwing here
  // would abort setupTelemetry before registerInstrumentations and drop RUM entirely.
  if (typeof getCurrentSpan !== 'function') {
    console.debug('[App] _getCurrentSpan absent — zone.get guard not applied');
    return;
  }
  internals._getCurrentSpan = (zone: unknown) =>
    typeof (zone as { get?: unknown } | null | undefined)?.get === 'function'
      ? getCurrentSpan.call(internals, zone)
      : undefined;
};

// Mirrors the Sumo wrapper's URL handling: strip any signal path already present on the configured
// collector URL, then append the signal we're exporting.
const signalEndpoint = (collectionSourceUrl: string, signal: 'traces' | 'logs'): string =>
  `${collectionSourceUrl.replace(/\/v1\/(traces|metrics|logs)\/?$/, '').replace(/\/+$/, '')}/v1/${signal}`;

// Wrap the exporter to scrub URL attrs on every span before export (onEnd can't — a ReadableSpan is read-only there) (150354).
class UrlScrubbingSpanExporter implements SpanExporter {
  public constructor(private readonly inner: SpanExporter) { }
  public export(spans: ReadableSpan[], resultCallback: (result: ExportResult) => void): void {
    for (const span of spans) {
      for (const key of Object.keys(span.attributes).filter(isUrlSpanAttribute)) {
        const value = span.attributes[key];
        if (typeof value !== 'string') continue;
        // url.query holds the query string alone (no '?'), so scrubUrl can't split it — drop it entirely.
        if (key === 'url.query') {
          delete (span.attributes as Record<string, unknown>)[key];
          continue;
        }
        (span.attributes as Record<string, unknown>)[key] = scrubUrl(value);
      }
    }
    this.inner.export(spans, resultCallback);
  }
  public shutdown(): Promise<void> {
    return this.inner.shutdown();
  }
  public forceFlush(): Promise<void> {
    return this.inner.forceFlush ? this.inner.forceFlush() : Promise.resolve();
  }
}

const STACK_LIMIT = 5000;
// The wrapper only attached the raw console.error arguments once the call looked structured rather
// than a plain formatted string: more than three arguments, or any non-Error object among them.
const CONSOLE_ARGS_LIMIT = 3;

// Free-text scrubUrl for error logs: drop any URL or path's query + fragment (keeping a stack frame's :line:col) and mask JWTs (151000).
export const scrubText = (text: string): string => text
  .replace(/(\/[^\s/?#"'`<>]*)[?#][^\s"'`<>]*?((?::\d+){0,2}[):]?)(?=[\s"'`<>]|$)/g, '$1$2')
  // Aplos: mask the finish-pex-login session id here too; no leading slash required, as router errors omit it.
  .replace(/(finish-pex-login\/)[^\s/?#"'`<>]+/gi, '$1{sessionId}')
  // Aplos: mask credential values by key as well (JSON, query or URL-encoded), since structured console.error args need not be absolute URLs.
  .replace(/((?:sessionId|jwt|token|accessToken|aplosPrivateKey|privateKey)(?:["']?\s*[:=]\s*["']?|%3D))[^\s&"',}<>\\%]+/gi, '$1[REDACTED]')
  // Match the whole eyJ run, then count segments — a strict JWT regex backtracks quadratically on long runs (151000).
  .replace(/eyJ[\w.-]*/g, (run: string): string => (run.split('.').length > 2 ? '[REDACTED]' : run));

// HttpErrorResponse implements Error without extending it, so instanceof misses it (151000).
const isErrorLike = (value: unknown): value is Error =>
  typeof value === 'object' && value !== null
    && typeof (value as Partial<Error>).name === 'string' && typeof (value as Partial<Error>).message === 'string';

const describeArg = (value: unknown): string => {
  if (typeof value === 'string') {
    return value;
  }
  if (isErrorLike(value)) {
    return `${value.name}: ${value.message}`;
  }
  try {
    return JSON.stringify(value) ?? String(value);
  } catch {
    return String(value);
  }
};

/**
 * Collects errors the way `collectErrors: true` did in the Sumo wrapper: as OTLP **log records** on
 * the collector's `/v1/logs` endpoint (severity ERROR, body = the message, with `type` /
 * `http.url` / `error.*` attributes), not as spans — Sumo's RUM error views read logs, so spans
 * would leave errors invisible there. All four of the wrapper's sources are covered:
 * `uncaughtException`, `unhandledRejection`, `documentError` and `consoleError`.
 */
const registerErrorHandlers = (resource: Resource, config: SumoTelemetry): void => {
  const loggerProvider = new LoggerProvider({
    resource: resource.merge(resourceFromAttributes({ ['http.user_agent']: navigator.userAgent })),
    processors: [
      // The browser build of this processor auto-flushes on visibilitychange/pagehide, so queued
      // error records survive navigation and tab close as they did under the Sumo wrapper.
      new BatchLogRecordProcessor({
        exporter: new OTLPLogExporter({ url: signalEndpoint(config.collectionSourceUrl, 'logs') })
      })
    ]
  });
  logs.setGlobalLoggerProvider(loggerProvider);

  const logger = logs.getLogger(RUM_SCOPE_NAME);

  // `type` reuses the wrapper's error taxonomy so existing Sumo queries keep matching.
  type ErrorType = 'uncaughtException' | 'unhandledRejection' | 'documentError' | 'consoleError';

  const record = (type: ErrorType, message: string, error?: Error, extra?: Attributes): void => {
    const attributes: Attributes = {
      type,
      ...extra,
      ['http.url']: scrubUrl(window.location.href)
    };
    if (config.collectSessionId) {
      attributes[SESSION_ATTRIBUTE] = getSessionId();
    }
    if (error) {
      attributes['error.name'] = error.name;
      attributes['error.message'] = scrubText(error.message);
      if (error.stack) {
        // Scrub before slicing — a JWT cut at STACK_LIMIT would slip past the mask (151000).
        attributes['error.stack'] = scrubText(error.stack).slice(0, STACK_LIMIT);
      }
    }
    logger.emit({ severityNumber: SeverityNumber.ERROR, severityText: 'ERROR', body: scrubText(message), attributes });
  };

  window.addEventListener('error', (event) => {
    record('uncaughtException', event.message || 'Unhandled error', isErrorLike(event.error) ? event.error : undefined);
  });

  window.addEventListener('unhandledrejection', (event) => {
    const reason: unknown = event.reason;
    const error = isErrorLike(reason) ? reason : undefined;
    const message = typeof reason === 'string' ? reason : (error?.message ?? 'Unhandled rejection');
    record('unhandledRejection', message, error);
  });

  // Failed subresources (img/script/link) fire a non-bubbling error event on the element itself, so
  // unlike the window handler above this has to listen in the capture phase.
  document.documentElement.addEventListener('error', (event) => {
    const target = event.target;
    if (!(target instanceof Element)) {
      return;
    }
    const xpath = getElementXPath(target, true);
    if (!xpath) {
      return;
    }
    record('documentError', `Uncaught error in element ${xpath}`, undefined, { ['element.xpath']: xpath });
  }, { capture: true });

  const originalConsoleError = console.error;
  let recordingConsoleError = false;
  console.error = (...args: unknown[]): void => {
    originalConsoleError.apply(console, args);
    // An exporter or emit failure that itself logs via console.error would otherwise recurse.
    if (recordingConsoleError) {
      return;
    }
    recordingConsoleError = true;
    try {
      const described = args.map(describeArg).map(scrubText);
      const structured = args.length > CONSOLE_ARGS_LIMIT
        || args.some((arg) => arg !== null && typeof arg === 'object' && !isErrorLike(arg));
      record(
        'consoleError',
        described.join(' '),
        args.find(isErrorLike),
        structured ? { arguments: described } : undefined
      );
    } finally {
      recordingConsoleError = false;
    }
  };
};

/**
 * Initialises browser RUM on the vendor-agnostic OTel v2 web SDK, exporting to the same Sumo
 * collector over OTLP/HTTP. Replaces `@sumologic/opentelemetry-rum`, whose bundle vendored OTel v1
 * including the CVE-2026-54285 `@opentelemetry/core@1.22.0` baggage propagator.
 */
export const setupTelemetry = (config: SumoTelemetry): void => {
  const corsUrls = (config.propagateTraceHeaderCorsUrls ?? []).map((url) => new RegExp(url));
  const samplingProbability = Number(config.samplingProbability ?? 1);

  // Merge onto the default resource so the SDK's telemetry.sdk.* attributes are retained
  // (resourceFromAttributes alone would replace, not extend, the default resource).
  const resource = defaultResource().merge(resourceFromAttributes({
    ['service.name']: config.serviceName,
    ['service.version']: environment.version,
    ['application']: config.applicationName,
    ['sampling.probability']: samplingProbability,
    ['deployment.environment']: config.deploymentEnvironment,
    ['deployment.region']: config.deploymentRegion,
    ['deployment.host_type']: config.deploymentHostType
  }));

  const spanProcessors: SpanProcessor[] = [
    new RumEnrichmentSpanProcessor(config.collectSessionId),
    new BatchSpanProcessor(new UrlScrubbingSpanExporter(new OTLPTraceExporter({ url: signalEndpoint(config.collectionSourceUrl, 'traces') })))
  ];

  const provider = new WebTracerProvider({
    resource,
    sampler: new ParentBasedSampler({ root: new TraceIdRatioBasedSampler(samplingProbability) }),
    spanProcessors
  });

  provider.register({
    contextManager: new ZoneContextManager(),
    propagator: new W3CTraceContextPropagator()
  });

  // Constructed disabled so the Zone.prototype patches never exist unguarded: the base constructor
  // would otherwise enable on construction. registerInstrumentations enables anything left disabled.
  const userInteractionInstrumentation = new UserInteractionInstrumentation({ enabled: false });
  guardZoneContextLookup(userInteractionInstrumentation);

  // Known parity gap vs the old Sumo RUM: Sumo's root-to-child enrichment added aggregate
  // XHR-timing attributes (http.time_in_xhr_calls / _to_first_xhr / _to_last_xhr /
  // _to_xhr_processing_end) onto every span. Those are not reproduced here — the individual
  // fetch/XHR spans and their timing events are still captured.
  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({ propagateTraceHeaderCorsUrls: corsUrls, ignoreUrls: IGNORE_URLS }),
      new XMLHttpRequestInstrumentation({ propagateTraceHeaderCorsUrls: corsUrls, ignoreUrls: IGNORE_URLS }),
      userInteractionInstrumentation,
      new LongTaskInstrumentation()
    ]
  });

  // The previous Sumo setup always ran with collectErrors: true.
  registerErrorHandlers(resource, config);
  console.debug('[App] Loaded telemetry');
};
