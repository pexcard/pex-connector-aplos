import { ComponentFixture, TestBed } from '@angular/core/testing';

import { VendorsManageComponent } from './vendors-manage.component';

describe('VendorsManageComponent', () => {
  let component: VendorsManageComponent;
  let fixture: ComponentFixture<VendorsManageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ VendorsManageComponent ]
    })
    .compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(VendorsManageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
