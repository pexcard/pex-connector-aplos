import { provideHttpClientTesting } from '@angular/common/http/testing';
import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { SyncHistoryComponent } from './sync-history.component';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';

describe('SyncHistoryComponent', () => {
  let component: SyncHistoryComponent;
  let fixture: ComponentFixture<SyncHistoryComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
    declarations: [SyncHistoryComponent],
    imports: [RouterTestingModule],
    providers: [
        { provide: 'BASE_URL', useValue: 'http://localhost:5001' },
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting()
    ]
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
