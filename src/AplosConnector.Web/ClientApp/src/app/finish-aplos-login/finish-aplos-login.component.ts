import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Component({
    selector: 'app-finish-aplos-login',
    templateUrl: './finish-aplos-login.component.html',
    styleUrls: ['./finish-aplos-login.component.css'],
    standalone: false
})
export class FinishAplosLoginComponent implements OnInit {

  constructor(private activatedRoute: ActivatedRoute, private authService: AuthService) { }

  code: string;
  state: string;
  realmId: string;
  //code=AB11571794434h1bM6b7LiDr5TOeTnI4ssnz6IzRHhYOHWNNyP&state=5ec5bc15-e2e1-4223-b862-62def958379f&realmId=123145996195294
  ngOnInit() {
    this.activatedRoute.queryParams.subscribe(
      params=>{
        console.log('query params', params);
        this.code = params.code;
        this.state = params.state;
        this.realmId = params.realmId;

        this.authService.createAplosToken(this.state, this.code, this.realmId);
      }
    );
  }

}
