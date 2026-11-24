import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormArray, FormControl } from '@angular/forms';
import { buttonLabelled } from '../../testing/button-labelled';
import { fieldLabelled } from '../../testing/field-labelled';
import { groupLabelled } from '../../testing/group-labelled';
import { typeInto } from '../../testing/type-into';
import { NameListField } from './name-list-field';

@Component({
  imports: [NameListField],
  template: `<app-name-list-field
    [names]="names()"
    legend="Authors"
    label="Author"
    [ordered]="ordered()"
  />`,
})
class Host {
  readonly names = input.required<FormArray<FormControl<string>>>();
  readonly ordered = input(true);
}

describe('NameListField', () => {
  function arrayOf(...values: string[]): FormArray<FormControl<string>> {
    return new FormArray(values.map((value) => new FormControl(value, { nonNullable: true })));
  }

  let fixture: ComponentFixture<Host>;

  async function show(names: FormArray<FormControl<string>>, ordered = true): Promise<HTMLElement> {
    fixture = TestBed.createComponent(Host);
    fixture.componentRef.setInput('names', names);
    fixture.componentRef.setInput('ordered', ordered);
    await fixture.whenStable();
    return fixture.nativeElement;
  }

  it('shows one field for each name', async () => {
    const page = await show(arrayOf('Ann', 'Bo'));

    expect(fieldLabelled(page, 'Author 1').value).toBe('Ann');
    expect(fieldLabelled(page, 'Author 2').value).toBe('Bo');
    expect(page.querySelector('legend')?.textContent).toBe('Authors');
  });

  it('keeps what is typed in the form array', async () => {
    const names = arrayOf('Ann');
    const page = await show(names);

    typeInto(fieldLabelled(page, 'Author 1'), 'Smith, Ann');

    expect(names.value).toEqual(['Smith, Ann']);
  });

  it('adds an empty field', async () => {
    const names = arrayOf('Ann');
    const page = await show(names);

    buttonLabelled(page, 'Add').click();
    await fixture.whenStable();

    expect(names.value).toEqual(['Ann', '']);
    expect(fieldLabelled(page, 'Author 2').value).toBe('');
  });

  it('removes the field beside the button', async () => {
    const names = arrayOf('Ann', 'Bo', 'Cy');
    const page = await show(names);

    buttonLabelled(groupLabelled(page, 'Author 2'), 'Remove').click();

    expect(names.value).toEqual(['Ann', 'Cy']);
  });

  it('moves a name up past the one before it', async () => {
    const names = arrayOf('Ann', 'Bo', 'Cy');
    const page = await show(names);

    buttonLabelled(groupLabelled(page, 'Author 3'), 'Move up').click();

    expect(names.value).toEqual(['Ann', 'Cy', 'Bo']);
  });

  it('moves a name down past the one after it', async () => {
    const names = arrayOf('Ann', 'Bo', 'Cy');
    const page = await show(names);

    buttonLabelled(groupLabelled(page, 'Author 1'), 'Move down').click();

    expect(names.value).toEqual(['Bo', 'Ann', 'Cy']);
  });

  it('disables the first name moving up and the last moving down', async () => {
    const page = await show(arrayOf('Ann', 'Bo'));

    expect(buttonLabelled(groupLabelled(page, 'Author 1'), 'Move up').disabled).toBe(true);
    expect(buttonLabelled(groupLabelled(page, 'Author 2'), 'Move down').disabled).toBe(true);
    expect(buttonLabelled(groupLabelled(page, 'Author 1'), 'Move down').disabled).toBe(false);
  });

  it('offers no move buttons when the order cannot change', async () => {
    const page = await show(arrayOf('Ann', 'Bo'), false);

    expect(() => buttonLabelled(page, 'Move up')).toThrow();
    expect(() => buttonLabelled(page, 'Move down')).toThrow();
    expect(buttonLabelled(page, 'Remove')).toBeTruthy();
  });
});
