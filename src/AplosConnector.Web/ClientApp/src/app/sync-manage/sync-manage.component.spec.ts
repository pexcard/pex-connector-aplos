import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SyncManageComponent } from './sync-manage.component';

describe('SyncManageComponent', () => {
  let component: SyncManageComponent;
  let fixture: ComponentFixture<SyncManageComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ SyncManageComponent ]
    })
    .compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(SyncManageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
