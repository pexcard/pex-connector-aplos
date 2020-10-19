import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { HandlePexJwtComponent } from './handle-pex-jwt.component';

describe('HandlePexJwtComponent', () => {
  let component: HandlePexJwtComponent;
  let fixture: ComponentFixture<HandlePexJwtComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ HandlePexJwtComponent ]
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
