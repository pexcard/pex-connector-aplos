import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { HeadlessComponent } from './headless.component';

describe('HeadlessComponent', () => {
  let component: HeadlessComponent;
  let fixture: ComponentFixture<HeadlessComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ HeadlessComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(HeadlessComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
