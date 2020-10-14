import { TestBed } from '@angular/core/testing';

import { HealthService } from './health.service';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('HealthService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports:[HttpClientTestingModule],
    providers:[{provide:'BASE_URL', useValue:'https://base.url'}]
  }));

  it('should be created', () => {
    const service: HealthService = TestBed.get(HealthService);
    expect(service).toBeTruthy();
  });
});
