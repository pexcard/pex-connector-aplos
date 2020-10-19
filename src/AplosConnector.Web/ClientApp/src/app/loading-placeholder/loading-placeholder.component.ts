import { Component, OnInit, Input, Output, EventEmitter } from '@angular/core';

@Component({
  selector: 'app-loading-placeholder',
  templateUrl: './loading-placeholder.component.html',
  styleUrls: ['./loading-placeholder.component.css']
})
export class LoadingPlaceholderComponent implements OnInit {
  
  @Input()
  loadingState = false;

  @Input()
  errorState = false;

  @Input()
  errorText = "An error occurred while attempting to process data";

  @Input()
  allowRetry = true;

  @Output()
  retry = new EventEmitter();

  constructor() { }

  ngOnInit() {
  }

  onRetry(){
    this.retry.emit();
  }

}
