import { TestBed, waitForAsync } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { AppComponent } from './app.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import {HttpClientTestingModule} from '@angular/common/http/testing'
import { ClarityModule } from '@clr/angular';

describe('AppComponent', () => {
  beforeEach(waitForAsync(() => {
    TestBed.configureTestingModule({
      imports: [
        RouterTestingModule, HttpClientTestingModule, ClarityModule
      ],
      schemas:[CUSTOM_ELEMENTS_SCHEMA],
      declarations: [
        AppComponent
      ],
      providers: [{ provide: 'BASE_URL', useValue: 'http://mock.url' }]
    }).compileComponents();
  }));

  it('should create the app', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const app = fixture.debugElement.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have as title 'PEX Connector for Aplos'`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const app = fixture.debugElement.componentInstance;
    expect(app.title).toEqual('PEX Connector for Aplos');
  });

  it('should render the header', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('clr-header')).toBeTruthy();
  });
});
