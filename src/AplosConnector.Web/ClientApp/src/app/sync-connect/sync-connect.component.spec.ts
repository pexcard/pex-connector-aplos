import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SyncConnectComponent } from './sync-connect.component';

describe('SyncConnectComponent', () => {
  let component: SyncConnectComponent;
  let fixture: ComponentFixture<SyncConnectComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ SyncConnectComponent ]
    })
    .compileComponents();
  });

  beforeEach(() => {
    fixture = TestBed.createComponent(SyncConnectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
