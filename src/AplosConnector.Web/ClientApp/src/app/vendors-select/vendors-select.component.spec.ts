import { ComponentFixture, TestBed } from '@angular/core/testing';

import { VendorsSelectComponent } from './vendors-select.component';

describe('VendorsSelectComponent', () => {
  let component: VendorsSelectComponent;
  let fixture: ComponentFixture<VendorsSelectComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ VendorsSelectComponent ]
    })
    .compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(VendorsSelectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
