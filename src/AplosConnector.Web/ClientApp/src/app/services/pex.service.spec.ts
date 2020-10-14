import { TestBed } from '@angular/core/testing';

import { PexService } from './pex.service';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('PexService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports:[HttpClientTestingModule],
    providers:[{provide:'BASE_URL', useValue:'https://base.url'}]
  }));

  it('should be created', () => {
    const service: PexService = TestBed.get(PexService);
    expect(service).toBeTruthy();
  });
});
