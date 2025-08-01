import { TestBed } from '@angular/core/testing';

import { MappingService } from './mapping.service';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('MappingService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [],
    providers: [{ provide: 'BASE_URL', useValue: 'https://base.url' }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()]
}));

  it('should be created', () => {
    const service: MappingService = TestBed.get(MappingService);
    expect(service).toBeTruthy();
  });
});
