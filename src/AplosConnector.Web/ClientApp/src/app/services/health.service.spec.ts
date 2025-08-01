import { TestBed } from '@angular/core/testing';

import { HealthService } from './health.service';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('HealthService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [],
    providers: [{ provide: 'BASE_URL', useValue: 'https://base.url' }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()]
}));

  it('should be created', () => {
    const service: HealthService = TestBed.get(HealthService);
    expect(service).toBeTruthy();
  });
});
