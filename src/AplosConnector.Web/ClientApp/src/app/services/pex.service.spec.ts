import { TestBed } from '@angular/core/testing';

import { PexService } from './pex.service';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('PexService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [],
    providers: [{ provide: 'BASE_URL', useValue: 'https://base.url' }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()]
}));

  it('should be created', () => {
    const service: PexService = TestBed.inject(PexService);
    expect(service).toBeTruthy();
  });
});
