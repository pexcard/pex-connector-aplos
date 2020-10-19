import { TestBed } from '@angular/core/testing';

import { AplosService } from './aplos.service';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('AplosService', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports:[HttpClientTestingModule],
    providers:[{provide:'BASE_URL', useValue:'https://base.url'}]
  }));

  it('should be created', () => {
    const service: AplosService = TestBed.get(AplosService);
    expect(service).toBeTruthy();
  });
});
