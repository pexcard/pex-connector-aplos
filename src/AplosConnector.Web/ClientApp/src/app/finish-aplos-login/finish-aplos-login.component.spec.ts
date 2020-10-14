import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { FinishAplosLoginComponent } from './finish-aplos-login.component';
import { ActivatedRoute } from '@angular/router';
import { RouterTestingModule } from '@angular/router/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

const mockActivatedRoute = {
  params: {
    get() { return '1'; },
    subscribe() {return null;}
  },
  queryParams:{
    subscribe(){return null;}
  }
};

xdescribe('FinishAplosLoginComponent', () => {
  let component: FinishAplosLoginComponent;
  let fixture: ComponentFixture<FinishAplosLoginComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ FinishAplosLoginComponent ],
      imports:[RouterTestingModule, HttpClientTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: mockActivatedRoute }, { provide: 'BASE_URL', useValue: 'http://mock.url' }],
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(FinishAplosLoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
