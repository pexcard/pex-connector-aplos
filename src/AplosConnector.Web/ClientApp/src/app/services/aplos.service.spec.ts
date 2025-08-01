import { TestBed } from '@angular/core/testing';

import { AplosService } from './aplos.service';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('AplosService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [],
    providers: [{ provide: 'BASE_URL', useValue: 'https://base.url' }, provideHttpClient(withInterceptorsFromDi()), provideHttpClientTesting()]
}));

  it('should be created', () => {
    const service: AplosService = TestBed.get(AplosService);
    expect(service).toBeTruthy();
  });
});
