import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { SyncHistoryComponent } from './sync-history.component';

describe('SyncHistoryComponent', () => {
  let component: SyncHistoryComponent;
  let fixture: ComponentFixture<SyncHistoryComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ SyncHistoryComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SyncHistoryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
