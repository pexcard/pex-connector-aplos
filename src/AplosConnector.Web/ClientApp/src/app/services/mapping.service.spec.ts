import { TestBed } from '@angular/core/testing';

import { MappingService } from './mapping.service';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('MappingService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports:[HttpClientTestingModule],
    providers:[{provide:'BASE_URL', useValue:'https://base.url'}]
  }));

  it('should be created', () => {
    const service: MappingService = TestBed.get(MappingService);
    expect(service).toBeTruthy();
  });
});
