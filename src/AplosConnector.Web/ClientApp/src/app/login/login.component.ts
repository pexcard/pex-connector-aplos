import { Component, OnInit } from '@angular/core';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit {

  constructor(private auth: AuthService) { }

  ngOnInit(): void {
  }

  onAuthenticateWithPex() {
    this.auth.getOauthURL();
  }

  onApply() {
    window.location.href = "https://apply.pexcard.com";
  }

}
