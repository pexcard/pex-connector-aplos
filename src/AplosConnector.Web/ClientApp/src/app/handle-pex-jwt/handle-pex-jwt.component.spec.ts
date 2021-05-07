import { HttpClientTestingModule } from '@angular/common/http/testing';
import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { HandlePexJwtComponent } from './handle-pex-jwt.component';

describe('HandlePexJwtComponent', () => {
  let component: HandlePexJwtComponent;
  let fixture: ComponentFixture<HandlePexJwtComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [HandlePexJwtComponent],
      imports: [HttpClientTestingModule, RouterTestingModule],
      providers: [
        { provide: 'BASE_URL', useValue: 'http://localhost:5001' }
      ]
    })
      .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(HandlePexJwtComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
