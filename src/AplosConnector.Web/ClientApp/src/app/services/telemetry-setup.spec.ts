import { scrubText, scrubUrl } from './telemetry-setup';

describe('telemetry-setup scrubbing', () => {
  const sessionId = '3f2b8c1e-0000-4000-8000-000000000000';
  const jwt = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln';
  const apiUrl = `https://aplos.example/api/Mapping/Settings?sessionId=${sessionId}`;

  it('strips queries and masks the session path in URLs', () => {
    expect(scrubUrl(`https://aplos.example/handle-pex-jwt?jwt=${jwt}`)).toBe('https://aplos.example/handle-pex-jwt');
    expect(scrubUrl(`https://aplos.example/finish-pex-login/${sessionId}`)).toBe('https://aplos.example/finish-pex-login/{sessionId}');
  });

  const leakyTexts: [string, string][] = [
    ['an HttpErrorResponse message', `Http failure response for ${apiUrl}: 500 Internal Server Error`],
    ['a response object with an absolute url', JSON.stringify({ url: apiUrl, status: 500, ok: false })],
    ['a relative url without a slash', JSON.stringify({ url: `Validity?sessionId=${sessionId}` })],
    ['a bare sessionId key', JSON.stringify({ sessionId, status: 401 })],
    ['a URL-encoded return url', JSON.stringify({ returnUrl: encodeURIComponent(`/sync-manage?sessionId=${sessionId}`) })],
    ['a router error on the session route', `Cannot match any routes. URL Segment: 'finish-pex-login/${sessionId}'`],
    ['a private key field', JSON.stringify({ aplosClientId: 'client', aplosPrivateKey: 'MIIEsecret' })],
    ['a raw JWT', `token ${jwt}`]
  ];

  for (const [name, text] of leakyTexts) {
    it(`removes credentials from ${name}`, () => {
      const scrubbed = scrubText(text);
      expect(scrubbed).not.toContain(sessionId);
      expect(scrubbed).not.toContain(jwt);
      expect(scrubbed).not.toContain('MIIEsecret');
    });
  }

  it('keeps non-credential context', () => {
    expect(scrubText(JSON.stringify({ url: apiUrl, status: 500 }))).toBe('{"url":"https://aplos.example/api/Mapping/Settings","status":500}');
  });
});
