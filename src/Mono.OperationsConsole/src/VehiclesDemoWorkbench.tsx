import { useMemo, useState } from 'react'
import './vehicles-demo-workbench.css'

const vehicles = [
  {id:'VEH-204',type:'موتورسیکلت',courier:'CR-204',zone:'مرکزی',plate:'۱۲الف۳۴۵',status:'فعال'},
  {id:'VEH-118',type:'موتورسیکلت',courier:'CR-118',zone:'شمال',plate:'۲۱ب۴۵۶',status:'فعال'},
  {id:'VEH-302',type:'خودرو',courier:'CR-302',zone:'جنوب',plate:'۴۴ج۷۸۹',status:'فعال'},
  {id:'VEH-225',type:'موتورسیکلت',courier:'CR-225',zone:'شرق',plate:'۷۱د۲۳۴',status:'فعال'},
  {id:'VEH-091',type:'خودرو',courier:'CR-091',zone:'غرب',plate:'۵۵ه۹۱۲',status:'مدارک'},
  {id:'VEH-144',type:'موتورسیکلت',courier:'CR-144',zone:'مرکزی',plate:'۳۳و۶۷۸',status:'تعلیق'},
]

export default function VehiclesDemoWorkbench(){
 const [selectedId,setSelectedId]=useState('VEH-204'),[query,setQuery]=useState(''),[filter,setFilter]=useState('همه')
 const visible=useMemo(()=>vehicles.filter(v=>(filter==='همه'||(filter==='موتور'?v.type==='موتورسیکلت':filter==='خودرو'?v.type==='خودرو':filter==='ریسک'?v.status!=='فعال':v.status==='فعال'))&&`${v.id} ${v.courier} ${v.plate}`.toLowerCase().includes(query.toLowerCase())),[filter,query])
 const selected=vehicles.find(v=>v.id===selectedId)||vehicles[0]
 return <main className="vehicles-demo" dir="rtl">
  <header className="vehicles-demo-heading"><div><h1>وسایل نقلیه</h1><p>ثبت وسیله، ظرفیت حمل، مدارک، پیک مرتبط و آمادگی برای تخصیص</p></div><div><button className="secondary-button" disabled>بازبینی مدارک</button><button className="primary-button" disabled>ثبت وسیله</button></div></header>
  <div className="vehicles-demo-banner">دادهٔ نمایشی وسایل نقلیه · فهرست و اقدام‌های واقعی هنوز به API مستقل متصل نیستند.</div>
  <section className="vehicles-demo-kpis">{[['وسایل ثبت‌شده','۷۲'],['موتورسیکلت فعال','۵۲'],['خودرو فعال','۱۶'],['مدرک نزدیک انقضا','۳'],['غیرفعال / تعلیق','۴']].map(([k,v])=><article key={k}><small>{k}</small><strong>{v}</strong></article>)}</section>
  <div className="vehicles-demo-grid"><aside className="vehicles-demo-card"><header><h2>فهرست وسایل</h2><span>۷۲ وسیله</span></header><input aria-label="جست‌وجوی پلاک، شناسه یا پیک" placeholder="جست‌وجوی پلاک، شناسه یا پیک" value={query} onChange={e=>setQuery(e.target.value)} /><div className="vehicle-filters">{['همه','موتور','خودرو','ریسک','فعال'].map(x=><button key={x} className={filter===x?'active':''} onClick={()=>setFilter(x)}>{x}</button>)}</div><div className="vehicle-list">{visible.map(v=><button key={v.id} className={`vehicle-row ${selectedId===v.id?'selected':''}`} onClick={()=>setSelectedId(v.id)}><span><strong dir="ltr">{v.id}</strong><small>{v.type} • {v.courier} • منطقه {v.zone}</small><small>پلاک: {v.plate}</small></span><em className={v.status==='فعال'?'good':v.status==='تعلیق'?'bad':'warn'}>{v.status}</em></button>)}</div></aside>
  <section className="vehicles-demo-card vehicle-detail"><header><div><h2 dir="ltr">{selected.id} — {selected.type}</h2><p>پیک مرتبط: {selected.courier} • منطقه اصلی: {selected.zone} • پلاک: {selected.plate} • مالکیت: شخصی پیک</p></div><span className="vehicle-ready">آماده تخصیص</span></header><div className="vehicle-detail-grid"><section><h3>قابلیت و ظرفیت حمل</h3><dl>{[['ظرفیت مأموریت','۳'],['ظرفیت وزن','۱۸ کیلو'],['رده بار','S / M'],['حداکثر بسته','۳ سفارش'],['مایع','مجاز با محدودیت'],['شکننده','مجاز با احتیاط'],['حساس به دما','غیرفعال'],['ظرفیت فعلی مسیر','۶۷٪']].map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl></section><section><h3>آمادگی برای تخصیص</h3><dl>{[['پیک','CR-204'],['وضعیت پیک','آنلاین'],['مسیر فعال','RTE-071'],['مناطق مجاز','مرکزی، شمال'],['منطقه فعلی','مرکزی'],['سیاست سخت','پاس شده']].map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl><p className="vehicle-eligible">واجد شرایط مأموریت جدید · در صورت رعایت وزن، حجم، منطقه و استاندارد خدمت می‌تواند مأموریت جدید دریافت کند.</p></section></div><div className="vehicle-detail-grid"><section><h3>مدارک و اعتبار</h3>{[['کارت وسیله','۱۴۰۷/۰۴'],['بیمه','۱۴۰۶/۰۹'],['معاینه / بررسی فنی','۱۴۰۶/۰۲'],['تصویر وسیله','—'],['پلاک','—']].map(([k,v])=><div className="vehicle-doc" key={k}><strong>{k}</strong><span>{v}</span><em>تأییدشده</em></div>)}<button className="text-button" disabled>مشاهده مدارک</button></section><section><h3>کنترل عملیاتی</h3><dl>{[['وضعیت وسیله','فعال'],['آخرین بررسی','۲۳ روز پیش'],['رخداد مرتبط','ندارد'],['محدودیت فعال','ندارد'],['مالکیت','شخصی پیک']].map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}</dl><p className="vehicle-rule">قاعده محصول: ون، پیکاپ و کامیون بعداً به‌عنوان قابلیت اضافه می‌شوند؛ برای هر شیوهٔ حمل محصول جدا ساخته نمی‌شود.</p><div className="vehicle-actions"><button className="secondary-button" disabled>ویرایش وسیله</button><button className="secondary-button" disabled>تعلیق موقت</button></div></section></div></section></div>
 </main>
}
