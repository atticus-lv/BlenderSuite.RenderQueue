import { motion, Reorder, useDragControls } from 'framer-motion'
import { useState } from 'react'
import { siteConfig } from '../content/site'
import styles from './HomePage.module.css'

const initialItems = [
  { id: '1', name: 'forest_scene.blend', range: '1-500', active: true, progress: null },
  { id: '2', name: 'cube.blend', range: '1-250', active: true, progress: 80 },
  { id: '3', name: 'logo_reveal.blend', range: '1-100', active: false, progress: null },
]

function ReorderItem({ item, selectedId, setSelectedId, toggleItem }: any) {
  const controls = useDragControls()
  
  return (
    <Reorder.Item 
      key={item.id} 
      value={item} 
      dragListener={false}
      dragControls={controls}
      className={`${styles.visualTask} ${item.progress ? styles.active : ''} ${selectedId === item.id ? styles.selected : ''}`}
      onClick={() => setSelectedId(item.id)}
    >
      <div className={styles.statusIndicator} style={{ background: item.active ? '#39d353' : '#57606a' }} />
      <div className={styles.taskInfo}>
        <div style={{ fontWeight: 500 }}>{item.name}</div>
        <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>Scene {item.range} {item.progress ? <span style={{color: '#f8d210'}}>*</span> : ''}</div>
      </div>
      
      {item.progress !== null && (
        <div className={styles.progressBarMini}>
          <motion.div className={styles.progressInner} animate={{ width: ['0%', '100%'] }} transition={{ duration: 10, repeat: Infinity, ease: "linear" }} />
        </div>
      )}
      
      {item.progress === null && (
        <div 
          className={`${styles.toggle} ${!item.active ? styles.inactive : ''}`} 
          onClick={(e) => toggleItem(item.id, e)}
        />
      )}
      
      <div 
        className={styles.handle} 
        onPointerDown={(e) => {
          setSelectedId(item.id);
          controls.start(e);
        }}
      >
        {[...Array(12)].map((_,i)=><span key={i}/>)}
      </div>
    </Reorder.Item>
  )
}

export function HomePage() {
  const [items, setItems] = useState(initialItems)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const toggleItem = (id: string, e: React.MouseEvent) => {
    e.stopPropagation()
    setItems(items.map(i => i.id === id ? { ...i, active: !i.active } : i))
  }

  return (
    <motion.div className={styles.page} initial={{ opacity: 0 }} animate={{ opacity: 1 }}>
      <header className={styles.hero}>
        <div className={styles.heroContent}>
          <h1 className={styles.heroTitle}>渲染调度系统</h1>
          <p className={styles.summary}>
            基于 Avalonia 构建，运行于 Windows 与 macOS。支持 Blender 场景自动识别，提供任务排队、状态监控与渲染自动化处理。
          </p>
        </div>

        <Reorder.Group axis="y" values={items} onReorder={setItems} className={styles.heroVisual} style={{ listStyle: 'none', padding: 0 }}>
          {items.map((item) => (
            <ReorderItem 
              key={item.id} 
              item={item} 
              selectedId={selectedId} 
              setSelectedId={setSelectedId} 
              toggleItem={toggleItem} 
            />
          ))}
        </Reorder.Group>
      </header>

      <section className={styles.section}>
        <span className={styles.eyebrow}>Core Capabilities</span>
        <div className={styles.grid}>
          {[
            { t: '任务管理', d: '支持渲染任务的优先级排序、暂停与继续。可随时动态覆写渲染参数，无需重复提交。' },
            { t: 'Blender 扩展', d: '原生集成 Blender 交互扩展，支持场景信息一键提交，自动匹配环境配置。' },
            { t: '序列帧管线', d: '内置序列帧预览与自动化合成，支持渲染完成后触发电源管理策略，如自动关机或睡眠。' },
            { t: '运行监控', d: '全链路状态观测，实时反馈任务进度与底层渲染日志，掌握渲染现场每一处变化。' }
          ].map((item, i) => (
            <motion.article 
              key={i} 
              className={styles.item}
              whileHover={{ y: -4, borderColor: 'rgba(20, 150, 255, 0.5)' }}
            >
              <h3>{item.t}</h3>
              <p>{item.d}</p>
            </motion.article>
          ))}
        </div>
      </section>
    </motion.div>
  )
}
