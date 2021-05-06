import { HttpClientTestingModule } from '@angular/common/http/testing';
import { async, ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';

import { HeadlessComponent } from './headless.component';

describe('HeadlessComponent', () => {
  let component: HeadlessComponent;
  let fixture: ComponentFixture<HeadlessComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [HeadlessComponent],
      imports: [HttpClientTestingModule, RouterTestingModule],
      providers: [
        { provide: 'BASE_URL', useValue: 'http://localhost:5001' }
      ]
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
