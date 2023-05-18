import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges } from '@angular/core';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

@Component({
  selector: 'app-select-list',
  templateUrl: './select-list.component.html',
  styleUrls: ['./select-list.component.css']
})

export class SelectListComponent implements OnInit, OnDestroy {
  private _originalItems = [];
  private _allItems = [];

  @Input() set allItems(items: any[]) {
    this._originalItems = [...items];
    this.reset();
  };
  get allItems(): any[] {
    return this._allItems;
  }

  @Input() selectedItems = [];
  @Output() selectedItemsChange = new EventEmitter<any[]>();

  _displayFn = (item) => item;
  @Input() set displayFn(fn: (item: any) => any) {
    this._displayFn = fn;
  };
  _filterFn = (item) => item;
  @Input() set filterFn(fn: (item: any) => any) {
    this._filterFn = fn;
    this.filterAllItems(this.allItemsFilter);
    this.filterSelectedItems(this.selectedItemsFilter);
  };
  _sortFn = (itemA, itemB) => 0;
  @Input() set sortFn(fn: (itemA: any, itemB: any) => number) {
    this._sortFn = fn;
    this.sortList(this.allItems);
    this.sortList(this.allItemsFiltered);
    this.sortList(this.selectedItems);
    this.sortList(this.selectedItemsFiltered);
  };

  @Input() allItemsTitle = 'All';
  @Input() selectedItemsTitle = 'Selected';
  @Input() maxSelectedItems = undefined;

  allItemsFilter = undefined;
  allItemsFiltered = [];

  selectedItemsFilter = undefined;
  selectedItemsFiltered = [];

  private allItemsFilterChanged: Subject<string> = new Subject<string>();
  private allItemsFilterChangedSubscription: Subscription;

  private selectedItemsFilterChanged: Subject<string> = new Subject<string>();
  private selectedItemsFilterChangedSubscription: Subscription;

  constructor() {
    this.allItemsFilterChangedSubscription = this.allItemsFilterChanged
      .pipe(debounceTime(300))
      .subscribe((term) => {
        if (term === null || term === undefined || term === '') {
          this.allItemsFiltered = this.allItems;
        } else {
          this.allItemsFiltered = this.allItems?.filter(item => this._filterFn(item)?.toString()?.toLowerCase().includes(term.toLowerCase())) ?? [];
        }
      });
    this.selectedItemsFilterChangedSubscription = this.selectedItemsFilterChanged
      .pipe(debounceTime(300))
      .subscribe((term) => {
        if (term === null || term === undefined || term === '') {
          this.selectedItemsFiltered = this.selectedItems;
        } else {
          this.selectedItemsFiltered = this.selectedItems?.filter(item => this._filterFn(item)?.toString()?.toLowerCase()?.includes(term.toLowerCase())) ?? [];
        }
      });
  }

  ngOnInit() {
    this.reset();
  }

  ngOnDestroy() {
    this.allItemsFilterChangedSubscription.unsubscribe();
    this.selectedItemsFilterChangedSubscription.unsubscribe();
  }

  filterAllItems(term: string) {
    this.allItemsFilterChanged.next(term);
  }

  filterSelectedItems(term: string) {
    this.selectedItemsFilterChanged.next(term);
  }

  sortList(list: any[]) {
    return list.sort(this._sortFn);
  }

  selectOne(event: MouseEvent, item, list, selectedList, enforceMax) {
    if (event.ctrlKey || event.shiftKey) {
      if (!enforceMax || this.maxSelectedItems > 0 && (list.filter(x => x.selected).length + selectedList.length) < this.maxSelectedItems) {
        item.selected = true;
      } else {
        item.selected = false;
      }
    } else {
      list.forEach(item => item.selected = false);
      if (!enforceMax || this.maxSelectedItems > 0 && selectedList.length < this.maxSelectedItems) {
        item.selected = true;
      }
    }
  }

  selectMany(event: MouseEvent, item, list, selectedList, enforceMax) {
    if (event.buttons === 1 && (!enforceMax || this.maxSelectedItems > 0 && (list.filter(x => x.selected).length + selectedList.length) < this.maxSelectedItems)) {
      item.selected = true;
    }
  }

  moveSelectedToLeft() {
    const selected = this.selectedItems.filter(x => x.selected);
    this.allItems.push(...selected);
    selected.forEach(x => this.selectedItems.splice(this.selectedItems.indexOf(x), 1));
    this.sortList(this.allItems);
    this.selectedItemsChange.emit(this.selectedItems);
  }

  moveSelectedToRight() {
    const selected = this.allItems.filter(x => x.selected);
    this.selectedItems.push(...selected);
    selected.forEach(x => this.allItems.splice(this.allItems.indexOf(x), 1));
    this.sortList(this.selectedItems);
    this.selectedItemsChange.emit(this.selectedItems);
  }

  reset() {
    this._allItems = this.allItemsFiltered = [...this._originalItems];
    this._allItems.forEach(x => x.selected = false);

    this.sortList(this._allItems);

    this.selectedItems = this.selectedItemsFiltered = [];
    this.selectedItemsChange.emit(this.selectedItems);
  }

}
