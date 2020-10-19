import { TestBed, async } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { AppComponent } from './app.component';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import {HttpClientTestingModule} from '@angular/common/http/testing'
import { ClarityModule } from '@clr/angular';

describe('AppComponent', () => {
  beforeEach(async(() => {
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
    const app = fixture.debugElement.componentInstance;
    expect(app).toBeTruthy();
  });

  it(`should have as title 'aplos-pex-connector'`, () => {
    const fixture = TestBed.createComponent(AppComponent);
    const app = fixture.debugElement.componentInstance;
    expect(app.title).toEqual('aplos-pex-connector');
  });
});
