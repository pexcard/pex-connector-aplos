import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { FinishPexLoginComponent } from './finish-pex-login.component';
import { ActivatedRoute } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

const mockActivatedRoute = {
  params: {
    get() { return '1'; },
    subscribe() {return null;}
  }
};

describe('FinishPexLoginComponent', () => {
  let component: FinishPexLoginComponent;
  let fixture: ComponentFixture<FinishPexLoginComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ FinishPexLoginComponent ],
      imports:[RouterTestingModule, HttpClientTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: mockActivatedRoute }, { provide: 'BASE_URL', useValue: 'http://mock.url' }],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(FinishPexLoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
